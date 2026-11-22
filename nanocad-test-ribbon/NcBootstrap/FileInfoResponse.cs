using System.Text.Json.Serialization;

namespace NcBootstrap;

internal sealed class FileInfoResponse
{
	[JsonPropertyName("filename")]
	public string FileName { get; set; }

	[JsonPropertyName("modification_date")]
	public string ModificationDate { get; set; }

	[JsonPropertyName("size_bytes")]
	public long SizeBytes { get; set; }

	[JsonPropertyName("sha256")]
	public string Sha256 { get; set; }

	[JsonIgnore]
	public bool ExistsLocally { get; set; }
}
