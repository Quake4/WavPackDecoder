/*
** WavpackMetadata.cs
**
** Copyright (c) 2010-2016 Peter McQuillan
**
** All Rights Reserved.
**                       
** Distributed under the BSD Software License (see license.txt)  
***/

namespace WavPack
{
	class WavpackMetadata
	{
		internal int byte_length;
		internal byte[] data;
		internal byte id;
		internal bool hasdata;
		internal bool error;
		// we use this to determine if we have read all the metadata 
		// in a block by checking bytecount again the block length
		// ckSize is block size minus 8. WavPack header is 32 bytes long so we start at 24
		internal long bytecount = 24;

		internal bool copy_data()
		{
			if (!hasdata || byte_length <= 0) return false;
			if (data.Length != Defines.BITSTREAM_BUFFER_SIZE)
				return true;

			var new_data = new byte[byte_length];
			System.Array.Copy(data, new_data, byte_length);
			data = new_data;

			return true;
		}
	}
}