using System;

namespace Colabora.Api.Models
{
    public class LoginLockout
    {
        public long Id { get; set; }

        public string Username { get; set; } = null!;

        public string Ip { get; set; } = null!;

        public int FailCount { get; set; }

        public DateTime? LockedUntil { get; set; }

        public DateTime UpdatedAt { get; set; }

        public DateTime CreatedAt { get; set; }
    }
}

