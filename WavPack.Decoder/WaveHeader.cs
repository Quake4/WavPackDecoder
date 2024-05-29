/*
** WaveHeader.cs
**
** Copyright (c) 2010-2016 Peter McQuillan
**
** All Rights Reserved.
**                       
** Distributed under the BSD Software License (see license.txt)
***/

namespace WavPack.Decoder
{
	class WaveHeader
	{
		internal const uint Size = 16;

		internal ushort FormatTag, NumChannels;
		internal uint SampleRate, BytesPerSecond;
		internal ushort BlockAlign, BitsPerSample;

		internal byte[] AsBytes()
		{
			byte[] bytes = new byte[Size];

			// swap endians
			bytes[1] = (byte)(FormatTag >> 8);
			bytes[0] = (byte)FormatTag;

			// swap endians
			bytes[3] = (byte)(NumChannels >> 8);
			bytes[2] = (byte)NumChannels;

			// swap endians
			bytes[7] = (byte)(SampleRate >> 24);
			bytes[6] = (byte)(SampleRate >> 16);
			bytes[5] = (byte)(SampleRate >> 8);
			bytes[4] = (byte)SampleRate;

			// swap endians
			bytes[11] = (byte)(BytesPerSecond >> 24);
			bytes[10] = (byte)(BytesPerSecond >> 16);
			bytes[9] = (byte)(BytesPerSecond >> 8);
			bytes[8] = (byte)BytesPerSecond;

			// swap endians
			bytes[13] = (byte)(BlockAlign >> 8);
			bytes[12] = (byte)BlockAlign;

			// swap endians
			bytes[15] = (byte)(BitsPerSample >> 8);
			bytes[14] = (byte)BitsPerSample;

			return bytes;
		}
	}
}