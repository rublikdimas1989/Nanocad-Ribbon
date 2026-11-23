namespace NcBootstrap;

internal sealed class FileSyncCheckResult
{
	public string FileName { get; set; }

	public string LocalPath { get; set; }

	public bool ExistsLocally { get; set; }

	public string LocalSha256 { get; set; }

	public string ServerSha256 { get; set; }

	public bool IsOutdated { get; set; }

	public string ComparisonMode { get; set; }

	public string Error { get; set; }
}
