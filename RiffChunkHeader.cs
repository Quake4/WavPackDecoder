/*
** RiffChunkHeader.cs
**
** Copyright (c) 2010-2016 Peter McQuillan
**
** All Rights Reserved.
**                       
** Distributed under the BSD Software License (see license.txt)  
***/

namespace WavPack.Decoder
{
class RiffChunkHeader : ChunkHeader
{
	public RiffChunkHeader(uint size)
		: base("RIFF", size + 4) // 4 = WAVE
	{
	}

	internal override byte[] AsBytes()
	{
		byte[] bytes = new byte[Size + 4]; // 4 = WAVE

		bytes[0] = (byte)ckID[0];
		bytes[1] = (byte)ckID[1];
		bytes[2] = (byte)ckID[2];
		bytes[3] = (byte)ckID[3];

		// swap endians here
		bytes[7] = (byte)(ckSize >> 24);
		bytes[6] = (byte)(ckSize >> 16);
		bytes[5] = (byte)(ckSize >> 8);
		bytes[4] = (byte)ckSize;

		bytes[8] = (byte)'W';
		bytes[9] = (byte)'A';
		bytes[10] = (byte)'V';
		bytes[11] = (byte)'E';

		return bytes;
	}
}
}