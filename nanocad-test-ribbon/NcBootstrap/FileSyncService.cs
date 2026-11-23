using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace NcBootstrap;

internal sealed class FileSyncService
{
	private sealed class FileListResponse
	{
		[JsonPropertyName("status")]
		public string Status { get; set; }

		[JsonPropertyName("file_info")]
		public List<FileInfoResponse> FileInfo { get; set; }
	}

	private readonly string _baseUrl;

	private readonly string _syncFolder;

	public string SyncFolder => _syncFolder;

	public FileSyncService(SyncConfig config)
	{
		_baseUrl = config.ServerUrl.TrimEnd('/');
		_syncFolder = config.ResolvedSyncFolder;
		Directory.CreateDirectory(_syncFolder);
	}

	public List<FileInfoResponse> GetServerFileList(string fileType)
	{
		string url = _baseUrl + "/computation-status/" + fileType + "||" + Environment.UserName + "||" + Environment.UserDomainName + "||" + Environment.MachineName + "||" + DateTime.Now.TimeOfDay;
		string json = WinHttpSync.Send("GET", url, null, CurrentSession.SessionToken);
		FileListResponse fileListResponse = JsonSerializer.Deserialize<FileListResponse>(json);
		if (fileListResponse != null && (fileListResponse.Status == "completed" || fileListResponse.Status == "no found"))
		{
			return fileListResponse.FileInfo ?? new List<FileInfoResponse>();
		}
		throw new InvalidOperationException("сервер вернул статус " + ((fileListResponse == null) ? "<пусто>" : fileListResponse.Status));
	}

	public bool DownloadFile(FileInfoResponse serverFile)
	{
		if (serverFile == null || string.IsNullOrWhiteSpace(serverFile.FileName))
		{
			return false;
		}
		string fileName = serverFile.FileName;
		string url = _baseUrl + "/computation-status/get file||" + Environment.UserName + "||" + fileName;
		string json = WinHttpSync.Send("GET", url, null, CurrentSession.SessionToken);
		Dictionary<string, object> dictionary = JsonSerializer.Deserialize<Dictionary<string, object>>(json);
		if (dictionary == null || !dictionary.ContainsKey("file_data"))
		{
			return false;
		}
		byte[] array = Convert.FromBase64String(dictionary["file_data"].ToString());
		if (!string.IsNullOrWhiteSpace(serverFile.Sha256))
		{
			string text = HashUtils.Sha256(array);
			if (!HashUtils.EqualsSha256(text, serverFile.Sha256))
			{
				Log.Write("SHA256 НЕ СОВПАЛ для " + fileName + ": server=" + serverFile.Sha256 + ", downloaded=" + text + ". Старая локальная версия оставлена без изменений.");
				return false;
			}
		}
		string text2 = Path.Combine(_syncFolder, fileName);
		string text3 = text2 + ".download";
		try
		{
			File.WriteAllBytes(text3, array);
			if (!string.IsNullOrWhiteSpace(serverFile.Sha256))
			{
				string left = HashUtils.Sha256File(text3);
				if (!HashUtils.EqualsSha256(left, serverFile.Sha256))
				{
					TryDelete(text3);
					Log.Write("SHA256 временного файла НЕ СОВПАЛ для " + fileName + ". Старая локальная версия продолжает использоваться.");
					return false;
				}
			}
			if (fileName.EndsWith(".dll", StringComparison.OrdinalIgnoreCase) && File.Exists(text2))
			{
				string destFileName = text2 + ".ncupdate";
				File.Move(text3, destFileName, overwrite: true);
				QueuePendingReplace(fileName, serverFile.Sha256);
				Log.Write("DLL отличается по SHA256. Новая версия проверена и сохранена как .ncupdate; старая DLL будет удалена/заменена после перезапуска nanoCAD: " + fileName);
				return true;
			}
			try
			{
				BackupPreviousDll(text2);
				File.Move(text3, text2, overwrite: true);
				if (!string.IsNullOrWhiteSpace(serverFile.Sha256))
				{
					string left2 = HashUtils.Sha256File(text2);
					if (!HashUtils.EqualsSha256(left2, serverFile.Sha256))
					{
						RestorePreviousDll(text2);
						Log.Write("SHA256 конечного файла НЕ СОВПАЛ для " + fileName + ". Выполнен откат.");
						return false;
					}
				}
				Log.Write("SHA256 проверен, файл обновлён: " + fileName);
				return true;
			}
			catch (IOException)
			{
				string destFileName2 = text2 + ".ncupdate";
				File.Move(text3, destFileName2, overwrite: true);
				QueuePendingReplace(fileName, serverFile.Sha256);
				Log.Write("файл заблокирован; новая SHA256-проверенная версия отложена до перезапуска nanoCAD: " + fileName);
				return true;
			}
		}
		catch (Exception ex2)
		{
			TryDelete(text3);
			Log.Write("ошибка скачивания " + fileName + ": " + ex2.Message + ". Старая локальная версия оставлена.");
			return false;
		}
	}

	private static void BackupPreviousDll(string target)
	{
		if (target.EndsWith(".dll", StringComparison.OrdinalIgnoreCase) && File.Exists(target))
		{
			string destFileName = target + ".previous";
			File.Copy(target, destFileName, overwrite: true);
		}
	}

	private static void RestorePreviousDll(string target)
	{
		try
		{
			string text = target + ".previous";
			if (File.Exists(text))
			{
				File.Copy(text, target, overwrite: true);
			}
		}
		catch
		{
		}
	}

	private static void TryDelete(string path)
	{
		try
		{
			if (File.Exists(path))
			{
				File.Delete(path);
			}
		}
		catch
		{
		}
	}

	private void QueuePendingReplace(string fileName, string sha256)
	{
		string path = Path.Combine(_syncFolder, "NC_REPLACE_PENDING.txt");
		Dictionary<string, string> dictionary = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
		if (File.Exists(path))
		{
			string[] array = File.ReadAllLines(path);
			foreach (string text in array)
			{
				if (!string.IsNullOrWhiteSpace(text))
				{
					string[] array2 = text.Split(new char[1] { '|' }, 2);
					string text2 = array2[0].Trim();
					string value = ((array2.Length > 1) ? array2[1].Trim() : string.Empty);
					if (text2.Length > 0)
					{
						dictionary[text2] = value;
					}
				}
			}
		}
		dictionary[fileName] = sha256 ?? string.Empty;
		File.WriteAllLines(path, dictionary.Select((KeyValuePair<string, string> kv) => kv.Key + "|" + kv.Value));
	}

	public List<string> DeleteOrphanedLocalFiles(List<FileInfoResponse> serverFiles)
	{
		List<string> list = new List<string>();
		HashSet<string> hashSet = new HashSet<string>(serverFiles.Select((FileInfoResponse f) => f.FileName), StringComparer.OrdinalIgnoreCase);
		string[] files = Directory.GetFiles(_syncFolder);
		foreach (string path in files)
		{
			string fileName = Path.GetFileName(path);
			if (!fileName.Equals("NC_DELETE_PENDING.txt", StringComparison.OrdinalIgnoreCase) && !fileName.Equals("NC_REPLACE_PENDING.txt", StringComparison.OrdinalIgnoreCase) && !fileName.EndsWith(".ncupdate", StringComparison.OrdinalIgnoreCase) && !fileName.EndsWith(".download", StringComparison.OrdinalIgnoreCase) && !fileName.EndsWith(".previous", StringComparison.OrdinalIgnoreCase) && !hashSet.Contains(fileName))
			{
				try
				{
					File.Delete(path);
					list.Add(fileName);
				}
				catch (Exception ex)
				{
					list.Add(fileName);
					QueuePendingDelete(fileName);
					Log.Write("старый файл заблокирован; удаление отложено до перезапуска: " + fileName + " (" + ex.Message + ")");
				}
			}
		}
		return list;
	}

	private void QueuePendingDelete(string fileName)
	{
		try
		{
			string path = Path.Combine(_syncFolder, "NC_DELETE_PENDING.txt");
			HashSet<string> hashSet = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
			if (File.Exists(path))
			{
				string[] array = File.ReadAllLines(path);
				foreach (string text in array)
				{
					if (!string.IsNullOrWhiteSpace(text))
					{
						hashSet.Add(text.Trim());
					}
				}
			}
			hashSet.Add(fileName);
			File.WriteAllLines(path, hashSet);
		}
		catch (Exception ex)
		{
			Log.Write("не удалось записать отложенное удаление " + fileName + ": " + ex.Message);
		}
	}

	public FileSyncCheckResult CheckLocalFile(FileInfoResponse serverFile)
	{
		FileSyncCheckResult fileSyncCheckResult = new FileSyncCheckResult
		{
			FileName = (serverFile?.FileName ?? string.Empty),
			ServerSha256 = (serverFile?.Sha256 ?? string.Empty),
			ComparisonMode = "SHA256"
		};
		if (serverFile == null || string.IsNullOrWhiteSpace(serverFile.FileName))
		{
			fileSyncCheckResult.Error = "Некорректные данные файла от сервера.";
			fileSyncCheckResult.IsOutdated = true;
			return fileSyncCheckResult;
		}
		string text2 = (fileSyncCheckResult.LocalPath = Path.Combine(_syncFolder, serverFile.FileName));
		fileSyncCheckResult.ExistsLocally = File.Exists(text2);
		if (!fileSyncCheckResult.ExistsLocally)
		{
			fileSyncCheckResult.IsOutdated = true;
			fileSyncCheckResult.LocalSha256 = "<ФАЙЛ ОТСУТСТВУЕТ>";
			return fileSyncCheckResult;
		}
		try
		{
			if (!string.IsNullOrWhiteSpace(serverFile.Sha256))
			{
				fileSyncCheckResult.LocalSha256 = HashUtils.Sha256File(text2);
				fileSyncCheckResult.IsOutdated = !HashUtils.EqualsSha256(fileSyncCheckResult.LocalSha256, serverFile.Sha256);
				return fileSyncCheckResult;
			}
			fileSyncCheckResult.ComparisonMode = "SIZE/DATE";
			FileInfo fileInfo = new FileInfo(text2);
			if (serverFile.SizeBytes != fileInfo.Length)
			{
				fileSyncCheckResult.IsOutdated = true;
				return fileSyncCheckResult;
			}
			try
			{
				DateTime dateTime = DateTime.ParseExact(serverFile.ModificationDate, "yyyy-MM-dd HH:mm:ss", null);
				fileSyncCheckResult.IsOutdated = dateTime > fileInfo.LastWriteTimeUtc;
			}
			catch
			{
				fileSyncCheckResult.IsOutdated = false;
			}
			return fileSyncCheckResult;
		}
		catch (Exception ex)
		{
			fileSyncCheckResult.Error = ex.Message;
			fileSyncCheckResult.IsOutdated = true;
			return fileSyncCheckResult;
		}
	}

	public bool IsLocalOutdated(FileInfoResponse serverFile)
	{
		FileSyncCheckResult fileSyncCheckResult = CheckLocalFile(serverFile);
		Log.Write("SYNC CHECK " + fileSyncCheckResult.FileName + ": mode=" + fileSyncCheckResult.ComparisonMode + ", local=" + fileSyncCheckResult.LocalSha256 + ", server=" + fileSyncCheckResult.ServerSha256 + ", outdated=" + fileSyncCheckResult.IsOutdated + (string.IsNullOrWhiteSpace(fileSyncCheckResult.Error) ? string.Empty : (", error=" + fileSyncCheckResult.Error)));
		return fileSyncCheckResult.IsOutdated;
	}
}
