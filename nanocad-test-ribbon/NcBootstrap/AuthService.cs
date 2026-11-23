using System;
using System.Collections.Generic;
using System.Text.Json;

namespace NcBootstrap;

internal sealed class AuthService
{
	private readonly string _baseUrl;

	public AuthService(string baseUrl)
	{
		_baseUrl = baseUrl.TrimEnd('/');
	}

	public bool Login(string userName, string password)
	{
		userName = userName.Replace("\"", "");
		password = password.Replace("\"", "");
		CurrentSession.UserName = userName;
		string body = JsonSerializer.Serialize(new
		{
			username = userName,
			password = password
		});
		try
		{
			string json = WinHttpSync.Send("POST", _baseUrl + "/login", body, null, "application/json");
			Dictionary<string, object> dictionary = JsonSerializer.Deserialize<Dictionary<string, object>>(json);
			if (dictionary != null && dictionary.ContainsKey("status") && dictionary["status"].ToString() == "success")
			{
				CurrentSession.SessionToken = dictionary["session_token"].ToString();
				CurrentSession.IsAuthenticated = true;
				if (dictionary.ContainsKey("user"))
				{
					Dictionary<string, object> dictionary2 = JsonSerializer.Deserialize<Dictionary<string, object>>(dictionary["user"].ToString());
					if (dictionary2 != null && dictionary2.ContainsKey("role"))
					{
						CurrentSession.Role = dictionary2["role"].ToString();
					}
				}
				return true;
			}
		}
		catch (Exception ex)
		{
			Log.Write("login ошибка: " + ex.Message);
		}
		return false;
	}
}
