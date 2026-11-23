using System;
using System.IO;
using System.Security.Cryptography;

namespace NcBootstrap;

internal static class HashUtils
{
	internal static string Sha256(byte[] data)
	{
		using SHA256 sHA = SHA256.Create();
		return Convert.ToHexString(sHA.ComputeHash(data));
	}

	internal static string Sha256File(string path)
	{
		using FileStream inputStream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
		using SHA256 sHA = SHA256.Create();
		return Convert.ToHexString(sHA.ComputeHash(inputStream));
	}

	internal static bool EqualsSha256(string left, string right)
	{
		if (string.IsNullOrWhiteSpace(left) || string.IsNullOrWhiteSpace(right))
		{
			return false;
		}
		return string.Equals(left.Trim(), right.Trim(), StringComparison.OrdinalIgnoreCase);
	}
}
