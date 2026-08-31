using System;

namespace AIFarm.Npc
{
    public readonly struct ResidentId : IEquatable<ResidentId>, IComparable<ResidentId>
    {
        public const int MaximumLength = 64;

        private readonly string value;

        public ResidentId(string value)
        {
            string normalized = (value ?? string.Empty).Trim();
            if (!IsValidValue(normalized))
            {
                throw new ArgumentException(
                    $"ResidentId must contain 1-{MaximumLength} lowercase ASCII letters, digits, or hyphens.",
                    nameof(value));
            }

            this.value = normalized;
        }

        public string Value => value ?? string.Empty;

        public bool IsValid => IsValidValue(value);

        public int CompareTo(ResidentId other)
        {
            return string.Compare(Value, other.Value, StringComparison.Ordinal);
        }

        public bool Equals(ResidentId other)
        {
            return string.Equals(Value, other.Value, StringComparison.Ordinal);
        }

        public override bool Equals(object obj)
        {
            return obj is ResidentId other && Equals(other);
        }

        public override int GetHashCode()
        {
            return StringComparer.Ordinal.GetHashCode(Value);
        }

        public override string ToString()
        {
            return Value;
        }

        public static bool TryCreate(string value, out ResidentId residentId)
        {
            string normalized = (value ?? string.Empty).Trim();
            if (!IsValidValue(normalized))
            {
                residentId = default;
                return false;
            }

            residentId = new ResidentId(normalized);
            return true;
        }

        public static bool operator ==(ResidentId left, ResidentId right)
        {
            return left.Equals(right);
        }

        public static bool operator !=(ResidentId left, ResidentId right)
        {
            return !left.Equals(right);
        }

        private static bool IsValidValue(string candidate)
        {
            if (string.IsNullOrEmpty(candidate) || candidate.Length > MaximumLength)
            {
                return false;
            }

            for (int index = 0; index < candidate.Length; index++)
            {
                char current = candidate[index];
                bool valid = (current >= 'a' && current <= 'z') ||
                    (current >= '0' && current <= '9') ||
                    current == '-';
                if (!valid)
                {
                    return false;
                }
            }

            return true;
        }
    }

    public static class ResidentIds
    {
        public const string YayaValue = "resident-001";

        public static ResidentId Yaya => new ResidentId(YayaValue);
    }
}
