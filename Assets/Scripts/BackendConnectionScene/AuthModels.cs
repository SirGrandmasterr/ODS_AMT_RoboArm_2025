using System;

namespace UnityML.Auth
{
    [Serializable]
    public class TokenResponse
    {
        public string access_token;
        public string token_type;
    }

    [Serializable]
    public class RegisterResponse
    {
        public string username;
        public string message;
    }

    [Serializable]
    public class ErrorResponse
    {
        public string detail;
    }
}