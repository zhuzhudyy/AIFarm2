using System;

namespace AIFarm.Town
{
    public readonly struct TownLocationId : IEquatable<TownLocationId>, IComparable<TownLocationId>
    {
        public const int MaximumLength = 64;

        private readonly string value;

        public TownLocationId(string value)
        {
            string normalized = (value ?? string.Empty).Trim();
            if (!IsValidValue(normalized))
            {
                throw new ArgumentException(
                    $"TownLocationId must contain 1-{MaximumLength} lowercase ASCII letters, digits, or hyphens.",
                    nameof(value));
            }

            this.value = normalized;
        }

        public string Value => value ?? string.Empty;

        public bool IsValid => IsValidValue(value);

        public int CompareTo(TownLocationId other)
        {
            return string.Compare(Value, other.Value, StringComparison.Ordinal);
        }

        public bool Equals(TownLocationId other)
        {
            return string.Equals(Value, other.Value, StringComparison.Ordinal);
        }

        public override bool Equals(object obj)
        {
            return obj is TownLocationId other && Equals(other);
        }

        public override int GetHashCode()
        {
            return StringComparer.Ordinal.GetHashCode(Value);
        }

        public override string ToString()
        {
            return Value;
        }

        public static bool TryCreate(string value, out TownLocationId locationId)
        {
            string normalized = (value ?? string.Empty).Trim();
            if (!IsValidValue(normalized))
            {
                locationId = default;
                return false;
            }

            locationId = new TownLocationId(normalized);
            return true;
        }

        public static bool operator ==(TownLocationId left, TownLocationId right)
        {
            return left.Equals(right);
        }

        public static bool operator !=(TownLocationId left, TownLocationId right)
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
}
