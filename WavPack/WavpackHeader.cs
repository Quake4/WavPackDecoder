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
	internal byte track_no, index_no;
	internal uint total_samples, block_index, block_samples, flags;
	internal int crc;
	internal bool error; // means error
	internal long stream_position;
	internal long average_block_size;
}
}