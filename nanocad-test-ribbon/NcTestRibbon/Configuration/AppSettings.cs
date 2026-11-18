namespace NcTestRibbon.Configuration
{
  using System;
  using System.Collections.Generic;
  using System.IO;
  using System.Text.Encodings.Web;
  using System.Text.Json;
  using NcTestRibbon.Shell;

  /// <summary>
  /// Portable JSON settings under Tracing_cache\Settings\tracing.conf
  /// (End-variant AppSettings, path retargeted to cache).
  /// </summary>
  public static class AppSettings
  {
    public const string General = "General";
    public const string Pipe = "Pipe";
    public const string Rivers = "RiverTransitions";

    public static string FilePath => TracingCache.TracingConf;

    private static readonly object Sync = new object();
    private static SettingsDocument _document;

    internal static readonly JsonSerializerOptions JsonOptions = new JsonSerializerOptions
    {
      WriteIndented = true,
      Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
      ReadCommentHandling = JsonCommentHandling.Skip,
      AllowTrailingCommas = true
    };

    public static void Initialize()
    {
      lock (Sync)
      {
        if (_document != null)
        {
          return;
        }

        TracingCache.EnsureCreated();
        if (File.Exists(FilePath))
        {
          string text = File.ReadAllText(FilePath);
          // Shell may have written a placeholder text file — start fresh JSON.
          if (text.TrimStart().StartsWith("{", StringComparison.Ordinal))
          {
            SettingsDocument loaded = JsonSerializer.Deserialize<SettingsDocument>(text, JsonOptions);
            if (loaded != null && loaded.FormatVersion == 1 && loaded.Sections != null)
            {
              _document = loaded;
              return;
            }
          }
        }

        _document = new SettingsDocument();
        WriteAtomically(_document);
      }
    }

    public static SettingsSection OpenSection(string name)
    {
      lock (Sync)
      {
        Initialize();
        Dictionary<string, JsonElement> values = _document.Sections.TryGetValue(name, out Dictionary<string, JsonElement> section)
          ? new Dictionary<string, JsonElement>(section)
          : new Dictionary<string, JsonElement>();
        return new SettingsSection(name, values);
      }
    }

    internal static void Commit(string name, Dictionary<string, JsonElement> changes)
    {
      if (changes.Count == 0)
      {
        return;
      }

      lock (Sync)
      {
        Initialize();
        var next = new SettingsDocument();
        foreach (KeyValuePair<string, Dictionary<string, JsonElement>> item in _document.Sections)
        {
          next.Sections[item.Key] = new Dictionary<string, JsonElement>(item.Value);
        }

        if (!next.Sections.TryGetValue(name, out Dictionary<string, JsonElement> section))
        {
          next.Sections[name] = section = new Dictionary<string, JsonElement>();
        }

        foreach (KeyValuePair<string, JsonElement> item in changes)
        {
          section[item.Key] = item.Value;
        }

        WriteAtomically(next);
        _document = next;
      }
    }

    private static void WriteAtomically(SettingsDocument document)
    {
      string temp = FilePath + "." + Guid.NewGuid().ToString("N") + ".tmp";
      try
      {
        byte[] bytes = JsonSerializer.SerializeToUtf8Bytes(document, JsonOptions);
        using (var stream = new FileStream(temp, FileMode.CreateNew, FileAccess.Write, FileShare.None))
        {
          stream.Write(bytes, 0, bytes.Length);
          stream.Flush(true);
        }

        File.Move(temp, FilePath, overwrite: true);
      }
      finally
      {
        try
        {
          if (File.Exists(temp))
          {
            File.Delete(temp);
          }
        }
        catch
        {
        }
      }
    }
  }

  public sealed class SettingsDocument
  {
    public int FormatVersion { get; set; } = 1;
    public Dictionary<string, Dictionary<string, JsonElement>> Sections { get; set; } =
      new Dictionary<string, Dictionary<string, JsonElement>>();
  }

  public sealed class SettingsSection : IDisposable
  {
    private readonly string _name;
    private readonly Dictionary<string, JsonElement> _values;
    private readonly Dictionary<string, JsonElement> _pending = new Dictionary<string, JsonElement>();

    internal SettingsSection(string name, Dictionary<string, JsonElement> values)
    {
      _name = name;
      _values = values;
    }

    public object GetValue(string name, object defaultValue = null)
    {
      if (!_values.TryGetValue(name, out JsonElement value))
      {
        return defaultValue;
      }

      return value.ValueKind switch
      {
        JsonValueKind.String => value.GetString(),
        JsonValueKind.True => true,
        JsonValueKind.False => false,
        JsonValueKind.Number when value.TryGetInt32(out int i) => i,
        JsonValueKind.Number when value.TryGetInt64(out long l) => l,
        JsonValueKind.Number => value.GetDouble(),
        JsonValueKind.Null => defaultValue,
        _ => value.Clone()
      };
    }

    public T Get<T>(string name)
    {
      return _values.TryGetValue(name, out JsonElement value)
        ? value.Deserialize<T>(AppSettings.JsonOptions)
        : default;
    }

    public void SetValue(string name, object value)
    {
      JsonElement element = JsonSerializer.SerializeToElement(
        value,
        value?.GetType() ?? typeof(object),
        AppSettings.JsonOptions);
      _values[name] = element;
      _pending[name] = element;
    }

    public void Commit()
    {
      AppSettings.Commit(_name, _pending);
      _pending.Clear();
    }

    public void Dispose()
    {
    }
  }
}
