// -------------------------------------------------------------------------------------------------------------------------------------------------------------
// Author: 3dapi (https://github.com/3dapi)
// -------------------------------------------------------------------------------------------------------------------------------------------------------------

/// <summary>
/// WAV, MP3 오디오 재생을 위한 인터페이스.
/// </summary>
interface IG2Audio : IDisposable
{
	string FilePath { get; }

	void Play(bool isLooping = false);
	bool IsPlaying();
	void Stop();
}

class G2Audio : IDisposable
{
	private enum AUDIO_TYPE { WAV, MP3 }

	private readonly IG2Audio _audio;
	public string FilePath => _audio.FilePath;

	public G2Audio(string filePath)
	{
		if (string.IsNullOrWhiteSpace(filePath))
		{
			throw new ArgumentException("G2Audio::파일 경로가 비어 있습니다.", nameof(filePath));
		}
		string path = G2Util.FindFilePath(filePath);
		_audio = GetAudioType(path) switch
		{
			AUDIO_TYPE.WAV => new G2AudioSound(path),
			AUDIO_TYPE.MP3 => new G2AudioMp3(path),
			_ => throw new InvalidDataException($"G2Audio::{path} 지원하지 않는 오디오 형식입니다.")
		};
	}

	public void Play(bool isLooping = false)
	{
		_audio.Play(isLooping);
	}

	public bool IsPlaying()
	{
		return _audio.IsPlaying();
	}

	public void Stop()
	{
		_audio.Stop();
	}

	public void Dispose()
	{
		_audio.Dispose();
	}

	private static AUDIO_TYPE GetAudioType(string filePath)
	{
		using FileStream stream = File.OpenRead(filePath);
		Span<byte> header = stackalloc byte[12];
		var readSize = stream.Read(header);
		if (readSize >= 12 && IsWav(header))
		{
			return AUDIO_TYPE.WAV;
		}
		if (readSize >= 10 && IsId3(header))
		{
			var framePosition = GetId3EndPosition(header);
			if (framePosition + 4 <= stream.Length)
			{
				stream.Position = framePosition;

				Span<byte> frameHeader = stackalloc byte[4];
				if (stream.Read(frameHeader) == frameHeader.Length &&
					IsMp3FrameHeader(frameHeader))
				{
					return AUDIO_TYPE.MP3;
				}
			}
		}
		else if (readSize >= 4 && IsMp3FrameHeader(header))
		{
			return AUDIO_TYPE.MP3;
		}
		throw new InvalidDataException($"G2Audio::{filePath} WAV 또는 MP3 파일이 아닙니다.");
	}

	private static bool IsWav(ReadOnlySpan<byte> header)
	{
		return
			header[ 0] == (byte)'R' &&
			header[ 1] == (byte)'I' &&
			header[ 2] == (byte)'F' &&
			header[ 3] == (byte)'F' &&
			header[ 8] == (byte)'W' &&
			header[ 9] == (byte)'A' &&
			header[10] == (byte)'V' &&
			header[11] == (byte)'E';
	}

	private static bool IsId3(ReadOnlySpan<byte> header)
	{
		return
			header[0] == (byte)'I' &&
			header[1] == (byte)'D' &&
			header[2] == (byte)'3';
	}

	private static long GetId3EndPosition(ReadOnlySpan<byte> header)
	{
		int tagSize =
			((header[6] & 0x7F) << 21) |
			((header[7] & 0x7F) << 14) |
			((header[8] & 0x7F) <<  7) |
			(header[9] & 0x7F);
		var position = 10L + tagSize;
		// ID3v2.4 footer가 있으면 10바이트 건너뜀.
		if (header[3] == 4 && (header[5] & 0x10) != 0)
		{
			position += 10;
		}
		return position;
	}

	private static bool IsMp3FrameHeader(ReadOnlySpan<byte> header)
	{
		if (header.Length < 4)
		{
			return false;
		}
		// MPEG Audio frame sync: 11 bits
		if (header[0] != 0xFF || (header[1] & 0xE0) != 0xE0)
		{
			return false;
		}
		var version = (header[1] >> 3) & 0x03;
		if (version == 1)
		{
			// MPEG version 01은 예약 값.
			return false;
		}
		var layer = (header[1] >> 1) & 0x03;
		if (layer != 1)
		{
			// Layer III만 MP3로 처리.
			return false;
		}
		var bitrateIndex = (header[2] >> 4) & 0x0F;
		if (bitrateIndex == 0 || bitrateIndex == 0x0F)
		{
			return false;
		}
		var sampleRateIndex = (header[2] >> 2) & 0x03;
		if (sampleRateIndex == 3)
		{
			return false;
		}
		return true;
	}
}
