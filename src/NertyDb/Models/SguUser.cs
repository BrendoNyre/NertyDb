using System;

namespace NertyDb.Models
{
    public class SguAuthResult
    {
        public bool IsSuccess { get; set; }
        public int UserCode { get; set; }
        public string UserName { get; set; } = string.Empty;
        public string GroupName { get; set; } = string.Empty;
        public string Status { get; set; } = "A";
        public string? ErrorMessage { get; set; }

        public static SguAuthResult Success(int userCode, string userName, string groupName) => new()
        {
            IsSuccess = true,
            UserCode = userCode,
            UserName = userName,
            GroupName = groupName,
            Status = "A"
        };

        public static SguAuthResult Failure(string message) => new()
        {
            IsSuccess = false,
            ErrorMessage = message
        };
    }
}
