using System.Numerics;
using System.Text;

namespace Iqb.Responses;

/// <summary>
/// Exact conversion of <see cref="double"/> to decimal digits that behaves identically on every runtime.
/// .NET Framework does not format doubles with more than 15 significant digits correctly, so the netstandard2.0
/// build cannot rely on the runtime for ECMAScript-compatible results.
/// </summary>
internal static class DoubleConversion
{
    /// <summary>
    /// Returns the digits of the shortest decimal that reads back as the finite, positive <paramref name="value"/>;
    /// among equally short ones the closest, and on a tie the even one, as ECMAScript's Number::toString requires.
    /// The value equals <c>0.Digits × 10^PointPosition</c>.
    /// </summary>
    /// <remarks>The free-format algorithm of Burger and Dybvig, using exact integer arithmetic.</remarks>
    internal static (string Digits, int PointPosition) ShortestDigits(double value)
    {
        var bits = BitConverter.DoubleToInt64Bits(value);
        var biasedExponent = (int)((bits >> 52) & 0x7FF);
        var fraction = bits & 0xFFFFFFFFFFFFFL;
        var mantissa = biasedExponent == 0 ? fraction : fraction | (1L << 52);
        var exponent = biasedExponent == 0 ? -1074 : biasedExponent - 1075;
        // A reader rounding half to even maps the interval boundaries to this value iff its mantissa is even.
        var inclusive = (mantissa & 1) == 0;
        // At an exact power of two the next smaller double is only half as far away as the next larger one.
        var asymmetric = fraction == 0 && biasedExponent > 1;

        // value = r / s; the boundaries of its rounding interval are (r - mMinus) / s and (r + mPlus) / s.
        BigInteger r, s, mPlus, mMinus;
        if (exponent >= 0)
        {
            var ulp = BigInteger.One << exponent;
            if (asymmetric)
            {
                r = mantissa * ulp * 4; s = 4; mPlus = ulp * 2; mMinus = ulp;
            }
            else
            {
                r = mantissa * ulp * 2; s = 2; mPlus = ulp; mMinus = ulp;
            }
        }
        else if (asymmetric)
        {
            r = new BigInteger(mantissa) * 4; s = BigInteger.One << (2 - exponent); mPlus = 2; mMinus = BigInteger.One;
        }
        else
        {
            r = new BigInteger(mantissa) * 2; s = BigInteger.One << (1 - exponent); mPlus = BigInteger.One; mMinus = BigInteger.One;
        }

        // Scale so that 0.1 <= upper boundary < 1, i.e. the first generated digit is the leading one.
        var pointPosition = (int)Math.Ceiling(Math.Log10(value) - 1e-10);
        if (pointPosition >= 0)
        {
            s *= BigInteger.Pow(10, pointPosition);
        }
        else
        {
            var scale = BigInteger.Pow(10, -pointPosition);
            r *= scale; mPlus *= scale; mMinus *= scale;
        }
        while (ReachesOne(r + mPlus))
        {
            s *= 10;
            pointPosition++;
        }
        while (!ReachesOne((r + mPlus) * 10))
        {
            r *= 10; mPlus *= 10; mMinus *= 10;
            pointPosition--;
        }

        var digits = new StringBuilder(17);
        while (true)
        {
            r *= 10; mPlus *= 10; mMinus *= 10;
            var digit = (int)BigInteger.DivRem(r, s, out r);
            var lowerFits = inclusive ? r <= mMinus : r < mMinus;
            var upperFits = ReachesOne(r + mPlus);
            if (!lowerFits && !upperFits)
            {
                digits.Append((char)('0' + digit));
                continue;
            }
            if (lowerFits && upperFits)
            {
                var comparison = (r * 2).CompareTo(s);
                if (comparison > 0 || (comparison == 0 && digit % 2 == 1)) digit++;
            }
            else if (upperFits)
            {
                digit++;
            }
            digits.Append((char)('0' + digit));
            return (digits.ToString(), pointPosition);
        }

        bool ReachesOne(BigInteger numerator) => inclusive ? numerator >= s : numerator > s;
    }
}
