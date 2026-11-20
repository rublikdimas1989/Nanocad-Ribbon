namespace NcTestRibbon.Net
{
  using System;
  using System.Collections.Generic;
  using System.IO;
  using System.Net;
  using System.Net.Http;
  using System.Text;
  using System.Text.Json;
  using System.Text.Json.Serialization;
  using System.Threading;
  using System.Threading.Tasks;
  using NcTestRibbon.Configuration;

  /// <summary>
  /// Async BUOS site-search jobs: POST BuosSearchPath → 202 job → poll statusUrl → GET resultUrl.
  /// Avoids System.Net.Http.Json under nanoCAD host.
  /// </summary>
  public static class BuosSearchClient
  {
    public const int PollIntervalMs = 1000;

    private static readonly object Gate = new object();
    private static readonly JsonSerializerOptions JsonWrite = new JsonSerializerOptions
    {
      PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
      DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
      WriteIndented = false
    };

    private static readonly JsonSerializerOptions JsonRead = new JsonSerializerOptions
    {
      PropertyNameCaseInsensitive = true,
      Converters = { new FlexibleInt32JsonConverter() }
    };

    /// <summary>
    /// Accepts JSON null, number, or string for int fields (completed/total).
    /// Missing/null/unparseable → 0 (UI treats total less than or equal to 0 as Marquee).
    /// </summary>
    internal sealed class FlexibleInt32JsonConverter : JsonConverter<int>
    {
      public override int Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
      {
        switch (reader.TokenType)
        {
          case JsonTokenType.Null:
            return 0;
          case JsonTokenType.Number:
            if (reader.TryGetInt32(out int i32))
            {
              return i32;
            }

            // Consume token; clamp out-of-range / fractional values.
            double dbl = reader.GetDouble();
            if (double.IsNaN(dbl) || double.IsInfinity(dbl))
            {
              return 0;
            }

            if (dbl > int.MaxValue)
            {
              return int.MaxValue;
            }

            if (dbl < int.MinValue)
            {
              return int.MinValue;
            }

            return (int)Math.Round(dbl);
          case JsonTokenType.String:
            string s = reader.GetString();
            if (string.IsNullOrWhiteSpace(s))
            {
              return 0;
            }

            if (int.TryParse(s.Trim(), System.Globalization.NumberStyles.Integer,
                  System.Globalization.CultureInfo.InvariantCulture, out int parsed))
            {
              return parsed;
            }

            if (double.TryParse(s.Trim(), System.Globalization.NumberStyles.Float,
                  System.Globalization.CultureInfo.InvariantCulture, out double dParsed)
                && !double.IsNaN(dParsed) && !double.IsInfinity(dParsed))
            {
              if (dParsed > int.MaxValue)
              {
                return int.MaxValue;
              }

              if (dParsed < int.MinValue)
              {
                return int.MinValue;
              }

              return (int)Math.Round(dParsed);
            }

            return 0;
          default:
            // Skip unexpected token shapes without failing the whole status poll.
            reader.Skip();
            return 0;
        }
      }

      public override void Write(Utf8JsonWriter writer, int value, JsonSerializerOptions options)
      {
        writer.WriteNumberValue(value);
      }
    }

    private static HttpClient _client;
    private static string _clientBase;

    public sealed class SearchBoundsDto
    {
      public double MinX { get; set; }
      public double MinY { get; set; }
      public double MaxX { get; set; }
      public double MaxY { get; set; }
    }

    public sealed class SiteDto
    {
      public double WidthM { get; set; }
      public double LengthM { get; set; }
      public double[] RotationAnglesDeg { get; set; }
    }

    public sealed class SearchDto
    {
      public double CenterStepM { get; set; }
      public double TerrainSampleStepM { get; set; }
    }

    public sealed class ConstraintsDto
    {
      public double RiverBufferM { get; set; }
      public string[] RiverLayers { get; set; }
      public string[] ForbiddenLayers { get; set; }
      public string PlacementLayer { get; set; }
    }

    public sealed class BuosSearchRequest
    {
      public string DatasetId { get; set; }
      public SearchBoundsDto SearchBounds { get; set; }
      public SiteDto Site { get; set; }
      public SearchDto Search { get; set; }
      public ConstraintsDto Constraints { get; set; }
    }

    public sealed class BuosSiteDto
    {
      public string Id { get; set; }
      public double X { get; set; }
      public double Y { get; set; }
      public double GroundElevationM { get; set; }
      public double ElevationRangeM { get; set; }
      public double RotationDeg { get; set; }

      /// <summary>Polygon ring as [x,y] pairs (closed or open).</summary>
      public List<double[]> Footprint { get; set; }
    }

    public sealed class BuosDiagnosticsDto
    {
      public int Centers { get; set; }
      public int ValidRotations { get; set; }
      public int RiverFeatures { get; set; }
      public int ForbiddenFeatures { get; set; }
      public int PlacementPoints { get; set; }
      public int PlacementAreas { get; set; }
      public string[] DxfLayers { get; set; }
    }

    public sealed class BuosSearchResponse
    {
      public List<BuosSiteDto> Sites { get; set; }
      public BuosDiagnosticsDto Diagnostics { get; set; }
    }

    /// <summary>202 Accepted body from POST search-jobs.</summary>
    public sealed class BuosJobAccepted
    {
      public string JobId { get; set; }
      public string StatusUrl { get; set; }
      public string ResultUrl { get; set; }
    }

    /// <summary>
    /// GET statusUrl body (polled every 1s). Fields match server 1:1:
    /// status, stage, message, completed, total, error.
    /// </summary>
    public sealed class BuosJobStatus
    {
      public string Status { get; set; }
      public string Stage { get; set; }
      public string Message { get; set; }

      [JsonConverter(typeof(FlexibleInt32JsonConverter))]
      public int Completed { get; set; }

      [JsonConverter(typeof(FlexibleInt32JsonConverter))]
      public int Total { get; set; }

      public string Error { get; set; }

      /// <summary>Optional; may appear on job create / some status payloads.</summary>
      public string JobId { get; set; }

      /// <summary>Optional override; otherwise use resultUrl from 202 Accepted.</summary>
      public string ResultUrl { get; set; }
    }

    public delegate void JobProgressHandler(BuosJobStatus status);

    public static BuosSearchResponse ParseResponse(string json)
    {
      if (string.IsNullOrWhiteSpace(json))
      {
        return new BuosSearchResponse { Sites = new List<BuosSiteDto>() };
      }

      BuosSearchResponse parsed = JsonSerializer.Deserialize<BuosSearchResponse>(json, JsonRead);
      if (parsed == null)
      {
        return new BuosSearchResponse { Sites = new List<BuosSiteDto>() };
      }

      parsed.Sites ??= new List<BuosSiteDto>();
      return parsed;
    }

    public static string FormatDiagnostics(BuosDiagnosticsDto d)
    {
      if (d == null)
      {
        return string.Empty;
      }

      return "centers=" + d.Centers
        + ", validRot=" + d.ValidRotations
        + ", rivers=" + d.RiverFeatures
        + ", forbidden=" + d.ForbiddenFeatures
        + ", placePts=" + d.PlacementPoints
        + ", placeAreas=" + d.PlacementAreas;
    }

    /// <summary>
    /// Build request from net.conf + geometry.conf + largest BUOS footprint.
    /// </summary>
    public static BuosSearchRequest BuildRequest(NetConf net, GeometryConf geo, double widthM, double lengthM)
    {
      if (net == null)
      {
        throw new ArgumentNullException(nameof(net));
      }

      if (geo == null || !geo.HasAoi)
      {
        throw new InvalidOperationException("Задайте область интереса (прямоугольник AOI) перед расчётом БУОС.");
      }

      if (string.IsNullOrWhiteSpace(net.DatasetId))
      {
        throw new InvalidOperationException(
          "В net.conf нет DatasetId. Сначала нажмите «Обновить карту на сервере» (успешная загрузка DXF).");
      }

      if (widthM <= 0 || lengthM <= 0 || !double.IsFinite(widthM) || !double.IsFinite(lengthM))
      {
        throw new InvalidOperationException(
          "Нет размеров БУОС. Откройте «Исходные данные» → БУОС и загрузите каталог.");
      }

      return new BuosSearchRequest
      {
        DatasetId = net.DatasetId.Trim(),
        SearchBounds = new SearchBoundsDto
        {
          MinX = geo.MinX,
          MinY = geo.MinY,
          MaxX = geo.MaxX,
          MaxY = geo.MaxY
        },
        Site = new SiteDto
        {
          WidthM = widthM,
          LengthM = lengthM,
          RotationAnglesDeg = net.ParseRotationAngles()
        },
        Search = new SearchDto
        {
          CenterStepM = net.CenterStepM > 0 ? net.CenterStepM : 50,
          TerrainSampleStepM = net.TerrainSampleStepM > 0 ? net.TerrainSampleStepM : 10
        },
        Constraints = new ConstraintsDto
        {
          RiverBufferM = geo.BuosRiverBufferM,
          RiverLayers = net.ParseLayerList(net.RiverLayers),
          ForbiddenLayers = net.ParseLayerList(net.ForbiddenLayers),
          PlacementLayer = string.IsNullOrWhiteSpace(net.BuosPlacementLayer)
            ? "Размещение БУОС"
            : net.BuosPlacementLayer.Trim()
        }
      };
    }

    /// <summary>
    /// POST job → poll statusUrl every 1s → on succeeded GET resultUrl and parse sites.
    /// </summary>
    public static async Task<BuosSearchResponse> SearchJobAsync(
      BuosSearchRequest request,
      JobProgressHandler onProgress,
      CancellationToken ct = default)
    {
      if (request == null)
      {
        throw new ArgumentNullException(nameof(request));
      }

      BuosJobAccepted job = await StartJobAsync(request, ct).ConfigureAwait(false);
      onProgress?.Invoke(new BuosJobStatus
      {
        Status = "queued",
        Message = "Задача создана (jobId=" + (job.JobId ?? "?") + ")",
        JobId = job.JobId,
        ResultUrl = job.ResultUrl
      });

      string statusUrl = RequireUrl(job.StatusUrl, "statusUrl");
      string resultUrl = job.ResultUrl;
      bool first = true;

      while (true)
      {
        ct.ThrowIfCancellationRequested();
        if (!first)
        {
          await Task.Delay(PollIntervalMs, ct).ConfigureAwait(false);
          ct.ThrowIfCancellationRequested();
        }

        first = false;

        BuosJobStatus status = await GetStatusAsync(statusUrl, ct).ConfigureAwait(false);
        if (!string.IsNullOrWhiteSpace(status.ResultUrl))
        {
          resultUrl = status.ResultUrl;
        }

        onProgress?.Invoke(status);

        string st = (status.Status ?? string.Empty).Trim();
        if (IsSucceeded(st))
        {
          string resolvedResult = RequireUrl(
            string.IsNullOrWhiteSpace(resultUrl) ? status.ResultUrl : resultUrl,
            "resultUrl");
          string body = await GetTextAsync(resolvedResult, ct).ConfigureAwait(false);
          return ParseResponse(body);
        }

        if (IsFailed(st))
        {
          string err = string.IsNullOrWhiteSpace(status.Error)
            ? (string.IsNullOrWhiteSpace(status.Message) ? "Расчёт БУОС завершился с ошибкой." : status.Message)
            : status.Error.Trim();
          throw new InvalidDataException(err);
        }
      }
    }

    private static async Task<BuosJobAccepted> StartJobAsync(BuosSearchRequest request, CancellationToken ct)
    {
      NetConf net = NetConf.Load();
      string path = string.IsNullOrWhiteSpace(net.BuosSearchPath)
        ? NetConf.DefaultBuosSearchPath
        : net.BuosSearchPath.Trim();

      string json = JsonSerializer.Serialize(request, JsonWrite);
      HttpClient client = GetClient();
      using var content = new StringContent(json, Encoding.UTF8, "application/json");
      using HttpResponseMessage response = await client.PostAsync(path, content, ct).ConfigureAwait(false);
      string body = await response.Content.ReadAsStringAsync().ConfigureAwait(false);
      ct.ThrowIfCancellationRequested();

      // Prefer 202; also accept 200 with job payload.
      if (response.StatusCode != HttpStatusCode.Accepted
          && response.StatusCode != HttpStatusCode.OK)
      {
        throw new InvalidDataException(
          "HTTP " + (int)response.StatusCode + " " + response.ReasonPhrase
          + " @ " + path
          + (string.IsNullOrWhiteSpace(body) ? string.Empty : (": " + Truncate(body, 400))));
      }

      BuosJobAccepted job = JsonSerializer.Deserialize<BuosJobAccepted>(body ?? string.Empty, JsonRead);
      if (job == null
          || string.IsNullOrWhiteSpace(job.StatusUrl)
          || (string.IsNullOrWhiteSpace(job.ResultUrl) && string.IsNullOrWhiteSpace(job.JobId)))
      {
        throw new InvalidDataException(
          "Ответ search-jobs без statusUrl/resultUrl: " + Truncate(body, 400));
      }

      return job;
    }

    private static async Task<BuosJobStatus> GetStatusAsync(string statusUrl, CancellationToken ct)
    {
      string body = await GetTextAsync(statusUrl, ct).ConfigureAwait(false);
      BuosJobStatus status = JsonSerializer.Deserialize<BuosJobStatus>(body ?? string.Empty, JsonRead);
      if (status == null)
      {
        throw new InvalidDataException("Пустой ответ statusUrl.");
      }

      return status;
    }

    private static async Task<string> GetTextAsync(string urlOrPath, CancellationToken ct)
    {
      HttpClient client = GetClient();
      using HttpResponseMessage response = await client.GetAsync(MakeRequestUri(urlOrPath), ct).ConfigureAwait(false);
      string body = await response.Content.ReadAsStringAsync().ConfigureAwait(false);
      ct.ThrowIfCancellationRequested();
      if (!response.IsSuccessStatusCode)
      {
        throw new InvalidDataException(
          "HTTP " + (int)response.StatusCode + " " + response.ReasonPhrase
          + " @ " + urlOrPath
          + (string.IsNullOrWhiteSpace(body) ? string.Empty : (": " + Truncate(body, 400))));
      }

      return body ?? string.Empty;
    }

    private static Uri MakeRequestUri(string urlOrPath)
    {
      string raw = (urlOrPath ?? string.Empty).Trim();
      if (raw.Length == 0)
      {
        throw new InvalidDataException("Пустой URL.");
      }

      if (Uri.TryCreate(raw, UriKind.Absolute, out Uri absolute))
      {
        return absolute;
      }

      while (raw.StartsWith("/", StringComparison.Ordinal))
      {
        raw = raw.Substring(1);
      }

      return new Uri(raw, UriKind.Relative);
    }

    private static string RequireUrl(string url, string name)
    {
      if (string.IsNullOrWhiteSpace(url))
      {
        throw new InvalidDataException("В ответе нет " + name + ".");
      }

      return url.Trim();
    }

    private static bool IsSucceeded(string status)
    {
      // Terminal per API: status == "succeeded"
      return status.Equals("succeeded", StringComparison.OrdinalIgnoreCase);
    }

    private static bool IsFailed(string status)
    {
      // Terminal per API: status == "failed"
      return status.Equals("failed", StringComparison.OrdinalIgnoreCase);
    }

    private static string Truncate(string text, int max)
    {
      if (string.IsNullOrEmpty(text) || text.Length <= max)
      {
        return text ?? string.Empty;
      }

      return text.Substring(0, max) + "…";
    }

    private static HttpClient GetClient()
    {
      NetConf conf = NetConf.Load();
      string baseAddr = NetConf.BuildBaseAddress(conf.Host);
      lock (Gate)
      {
        if (_client != null && string.Equals(_clientBase, baseAddr, StringComparison.OrdinalIgnoreCase))
        {
          return _client;
        }

        _client?.Dispose();
        _client = new HttpClient
        {
          BaseAddress = new Uri(baseAddr),
          Timeout = TimeSpan.FromMinutes(30)
        };
        _clientBase = baseAddr;
        return _client;
      }
    }
  }
}
