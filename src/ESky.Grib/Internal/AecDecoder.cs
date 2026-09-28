namespace ESky.Grib.Internal;

[Flags]
internal enum AecFlags : byte
{
    None = 0,
    Signed = 1,
    ThreeByte = 2,
    Msb = 4,
    Preprocess = 8,
    RestrictedCodes = 16,
    PadRsi = 32,
    NotEnforce = 64
}

internal readonly record struct AecConfig(
    int BitsPerSample,
    int BlockSize,
    int ReferenceSampleInterval,
    AecFlags Flags);

/// <summary>
/// Managed decoder for CCSDS 121.0-B adaptive entropy coding as used by
/// GRIB2 Data Representation Template 5.42.
/// </summary>
/// <remarks>
/// The implementation is a C# port of the libaec-compatible decoder in
/// pspoerri/go-tiled-eccodes (MIT). It implements whole-buffer decoding only,
/// which is the operation required by a GRIB2 Section 7 payload.
/// </remarks>
internal static class AecDecoder
{
    private const int SecondExtensionTableSize = 90;
    private static readonly int[] SecondExtensionTable = BuildSecondExtensionTable();

    public static uint[] Decode(ReadOnlySpan<byte> source, int sampleCount, AecConfig config)
    {
        if (sampleCount < 0)
            throw new ArgumentOutOfRangeException(nameof(sampleCount));

        Validate(config);

        if (sampleCount == 0)
            return [];

        var state = new DecoderState(source, sampleCount, config);
        state.Run();
        return state.Output;
    }

    private static void Validate(AecConfig config)
    {
        if (config.BitsPerSample is < 1 or > 32)
            throw new GribException("CCSDS/AEC bits per sample must be between 1 and 32.");

        if (config.ReferenceSampleInterval is < 1 or > 4096)
            throw new GribException("CCSDS/AEC reference sample interval must be between 1 and 4096.");

        if (config.BlockSize is < 2 or > 256 || (config.BlockSize & 1) != 0)
            throw new GribException("CCSDS/AEC block size must be an even value between 2 and 256.");

        if ((config.Flags & AecFlags.RestrictedCodes) != 0 &&
            config.BitsPerSample is >= 5 and <= 8)
        {
            throw new GribException(
                "CCSDS/AEC restricted-code mode is invalid for 5 to 8 bits per sample.");
        }
    }

    private static int GetIdLength(AecConfig config) =>
        config.BitsPerSample switch
        {
            > 16 => 5,
            > 8 => 4,
            _ when (config.Flags & AecFlags.RestrictedCodes) == 0 => 3,
            <= 2 => 1,
            _ => 2
        };

    private static int[] BuildSecondExtensionTable()
    {
        var table = new int[2 * (SecondExtensionTableSize + 1)];
        var k = 0;

        for (var i = 0; i < 13; i++)
        {
            var rowStart = k;
            for (var j = 0; j <= i && k <= SecondExtensionTableSize; j++)
            {
                table[2 * k] = i;
                table[2 * k + 1] = rowStart;
                k++;
            }
        }

        return table;
    }

    private ref struct DecoderState
    {
        private readonly AecConfig _config;
        private readonly int _idLength;
        private readonly uint _idMaximum;
        private readonly int _blockSize;
        private readonly int _rsiBlocks;
        private readonly int _rsiSize;
        private readonly bool _preprocess;
        private readonly bool _signed;
        private readonly bool _padRsi;
        private readonly uint _xmin;
        private readonly uint _xmax;
        private readonly int _needed;
        private readonly uint[] _rsiBuffer;

        private AecBitReader _reader;
        private int _rsiPosition;
        private int _emitted;

        public DecoderState(ReadOnlySpan<byte> source, int sampleCount, AecConfig config)
        {
            _config = config;
            _idLength = GetIdLength(config);
            _idMaximum = (1u << _idLength) - 1u;
            _blockSize = config.BlockSize;
            _rsiBlocks = config.ReferenceSampleInterval;
            _rsiSize = checked(config.ReferenceSampleInterval * config.BlockSize);
            _preprocess = (config.Flags & AecFlags.Preprocess) != 0;
            _signed = (config.Flags & AecFlags.Signed) != 0;
            _padRsi = (config.Flags & AecFlags.PadRsi) != 0;
            _needed = sampleCount;

            var unsignedMaximum = config.BitsPerSample == 32
                ? uint.MaxValue
                : (1u << config.BitsPerSample) - 1u;

            if (_signed)
            {
                _xmax = unsignedMaximum >> 1;
                _xmin = ~_xmax;
            }
            else
            {
                _xmin = 0;
                _xmax = unsignedMaximum;
            }

            _reader = new AecBitReader(source);
            _rsiBuffer = new uint[_rsiSize];
            _rsiPosition = 0;
            _emitted = 0;
            Output = new uint[sampleCount];
        }

        public uint[] Output { get; }

        public void Run()
        {
            while (_emitted + _rsiPosition < _needed)
            {
                DecodeBlock();

                if (_rsiPosition >= _rsiSize)
                {
                    Flush(_rsiSize);
                    _rsiPosition = 0;

                    if (_padRsi)
                        _reader.AlignToByte();
                }
            }

            if (_rsiPosition > 0)
            {
                Flush(_rsiPosition);
                _rsiPosition = 0;
            }
        }

        private void DecodeBlock()
        {
            var referenceSlot = _preprocess && _rsiPosition == 0 ? 1 : 0;
            var id = ReadBits(_idLength);

            if (id == 0)
            {
                DecodeLowEntropy(referenceSlot);
            }
            else if (id == _idMaximum)
            {
                DecodeUncompressed();
            }
            else
            {
                DecodeSplit(checked((int)id - 1), referenceSlot);
            }
        }

        private void DecodeUncompressed()
        {
            EnsureRsiCapacity(_blockSize);

            for (var i = 0; i < _blockSize; i++)
                _rsiBuffer[_rsiPosition++] = ReadBits(_config.BitsPerSample);
        }

        private void DecodeSplit(int k, int referenceSlot)
        {
            if (referenceSlot == 1)
            {
                EnsureRsiCapacity(1);
                _rsiBuffer[_rsiPosition++] = ReadBits(_config.BitsPerSample);
            }

            var encodedBlockSize = _blockSize - referenceSlot;
            EnsureRsiCapacity(encodedBlockSize);

            var start = _rsiPosition;
            for (var i = 0; i < encodedBlockSize; i++)
            {
                var fs = ReadFundamentalSequence();
                _rsiBuffer[start + i] = checked(fs << k);
            }

            if (k > 0)
            {
                for (var i = 0; i < encodedBlockSize; i++)
                    _rsiBuffer[start + i] += ReadBits(k);
            }

            _rsiPosition = start + encodedBlockSize;
        }

        private void DecodeLowEntropy(int referenceSlot)
        {
            var subId = ReadBits(1);

            if (referenceSlot == 1)
            {
                EnsureRsiCapacity(1);
                _rsiBuffer[_rsiPosition++] = ReadBits(_config.BitsPerSample);
            }

            if (subId == 1)
                DecodeSecondExtension(referenceSlot);
            else
                DecodeZeroBlock(referenceSlot);
        }

        private void DecodeZeroBlock(int referenceSlot)
        {
            var fs = ReadFundamentalSequence();
            const int remainderOfSegment = 5;

            var zeroBlocks = checked((int)fs + 1);

            if (zeroBlocks == remainderOfSegment)
            {
                var completedBlocks = _rsiPosition / _blockSize;
                zeroBlocks = Math.Min(
                    _rsiBlocks - completedBlocks,
                    64 - (completedBlocks % 64));
            }
            else if (zeroBlocks > remainderOfSegment)
            {
                zeroBlocks--;
            }

            var zeroSamples = checked(zeroBlocks * _blockSize - referenceSlot);

            if (zeroSamples < 0 || _rsiPosition + zeroSamples > _rsiSize)
                throw new GribException("Malformed CCSDS/AEC zero block.");

            Array.Clear(_rsiBuffer, _rsiPosition, zeroSamples);
            _rsiPosition += zeroSamples;
        }

        private void DecodeSecondExtension(int referenceSlot)
        {
            var i = referenceSlot;

            while (i < _blockSize)
            {
                var m = ReadFundamentalSequence();
                if (m > SecondExtensionTableSize)
                    throw new GribException("Malformed CCSDS/AEC second-extension value.");

                var index = checked((int)m);
                var d1 = index - SecondExtensionTable[2 * index + 1];

                if ((i & 1) == 0)
                {
                    EnsureRsiCapacity(1);
                    _rsiBuffer[_rsiPosition++] =
                        checked((uint)(SecondExtensionTable[2 * index] - d1));
                    i++;
                }

                EnsureRsiCapacity(1);
                _rsiBuffer[_rsiPosition++] = checked((uint)d1);
                i++;
            }
        }

        private void Flush(int count)
        {
            var outputCount = Math.Min(count, _needed - _emitted);
            if (outputCount <= 0)
                return;

            if (!_preprocess)
            {
                Array.Copy(_rsiBuffer, 0, Output, _emitted, outputCount);
                _emitted += outputCount;
                return;
            }

            var last = _rsiBuffer[0];

            if (_signed)
            {
                var signBit = 1u << (_config.BitsPerSample - 1);
                last = unchecked((last ^ signBit) - signBit);
            }

            _rsiBuffer[0] = last;
            var data = last;

            if (_xmin == 0)
            {
                var median = _xmax / 2 + 1;

                for (var i = 1; i < outputCount; i++)
                {
                    var difference = _rsiBuffer[i];
                    var halfDifference = (difference >> 1) + (difference & 1);
                    var mask = (data & median) != 0 ? _xmax : 0u;

                    if (halfDifference <= (mask ^ data))
                    {
                        var delta = (difference >> 1) ^
                                    ~unchecked((difference & 1u) - 1u);
                        data = unchecked(data + delta);
                    }
                    else
                    {
                        data = mask ^ difference;
                    }

                    _rsiBuffer[i] = data;
                }
            }
            else
            {
                for (var i = 1; i < outputCount; i++)
                {
                    var difference = _rsiBuffer[i];
                    var halfDifference = (difference >> 1) + (difference & 1);

                    if (unchecked((int)data) < 0)
                    {
                        if (halfDifference <= unchecked(_xmax + data + 1u))
                        {
                            var delta = (difference >> 1) ^
                                        ~unchecked((difference & 1u) - 1u);
                            data = unchecked(data + delta);
                        }
                        else
                        {
                            data = unchecked(difference - _xmax - 1u);
                        }
                    }
                    else
                    {
                        if (halfDifference <= _xmax - data)
                        {
                            var delta = (difference >> 1) ^
                                        ~unchecked((difference & 1u) - 1u);
                            data = unchecked(data + delta);
                        }
                        else
                        {
                            data = unchecked(_xmax - difference);
                        }
                    }

                    _rsiBuffer[i] = data;
                }
            }

            Array.Copy(_rsiBuffer, 0, Output, _emitted, outputCount);
            _emitted += outputCount;
        }

        private uint ReadBits(int bitCount)
        {
            if (!_reader.TryReadBits(bitCount, out var value))
                throw new GribException("CCSDS/AEC payload ended before all samples were decoded.");

            return value;
        }

        private uint ReadFundamentalSequence()
        {
            if (!_reader.TryReadFundamentalSequence(out var value))
                throw new GribException("CCSDS/AEC payload ended inside a fundamental sequence.");

            return value;
        }

        private void EnsureRsiCapacity(int additionalSamples)
        {
            if (_rsiPosition + additionalSamples > _rsiSize)
                throw new GribException("Malformed CCSDS/AEC stream exceeds its reference sample interval.");
        }
    }

    private ref struct AecBitReader
    {
        private readonly ReadOnlySpan<byte> _source;
        private int _position;
        private ulong _accumulator;
        private int _bitCount;

        public AecBitReader(ReadOnlySpan<byte> source)
        {
            _source = source;
            _position = 0;
            _accumulator = 0;
            _bitCount = 0;
        }

        public bool TryReadBits(int count, out uint value)
        {
            if (count is < 0 or > 32)
                throw new ArgumentOutOfRangeException(nameof(count));

            if (count == 0)
            {
                value = 0;
                return true;
            }

            while (_bitCount < count)
            {
                if (_position >= _source.Length)
                {
                    value = 0;
                    return false;
                }

                _accumulator = (_accumulator << 8) | _source[_position++];
                _bitCount += 8;
            }

            var shift = _bitCount - count;
            var mask = count == 32
                ? uint.MaxValue
                : (1u << count) - 1u;

            value = (uint)((_accumulator >> shift) & mask);
            _bitCount -= count;
            return true;
        }

        public bool TryReadFundamentalSequence(out uint value)
        {
            value = 0;

            while (TryReadBits(1, out var bit))
            {
                if (bit != 0)
                    return true;

                if (value == uint.MaxValue)
                    throw new GribException("CCSDS/AEC fundamental sequence is too long.");

                value++;
            }

            value = 0;
            return false;
        }

        public void AlignToByte() => _bitCount -= _bitCount % 8;
    }
}
