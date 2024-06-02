/*
** ChunkHeader.cs
**
** Copyright (c) 2010-2016 Peter McQuillan
**
** All Rights Reserved.
**                       
** Distributed under the BSD Software License (see license.txt)
***/

namespace WavPack.Decoder
{
class ChunkHeader
{
	internal const uint Size = 8;

	internal char[] ckID = new char[4];
	internal uint ckSize;

	public ChunkHeader(string id, uint size)
	{
		ckID[0] = id[0];
		ckID[1] = id[1];
		ckID[2] = id[2];
		ckID[3] = id[3];
		ckSize = size;
	}

	internal virtual byte[] AsBytes()
	{
		byte[] bytes = new byte[Size];

		bytes[0] = (byte)ckID[0];
		bytes[1] = (byte)ckID[1];
		bytes[2] = (byte)ckID[2];
		bytes[3] = (byte)ckID[3];

		// swap endians here
		bytes[7] = (byte)(ckSize >> 24);
		bytes[6] = (byte)(ckSize >> 16);
		bytes[5] = (byte)(ckSize >> 8);
		bytes[4] = (byte)ckSize;

		return bytes;
	}
}
}