/*
** WavpackHeader.cs
**
** Copyright (c) 2010-2016 Peter McQuillan
**
** All Rights Reserved.
**                       
** Distributed under the BSD Software License (see license.txt)  
***/

namespace WavPack
{
class WavpackHeader
{
	internal uint ckSize;
	internal short version;
	internal long total_samples, block_index;
	internal uint block_samples, flags;
	internal int crc;
	internal bool error; // means error
	internal long stream_position;
	internal long average_block_size;
}
}