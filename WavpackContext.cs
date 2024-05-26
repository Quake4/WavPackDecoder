/*
** WavpackContext.cs
**
** Copyright (c) 2010-2016 Peter McQuillan
**
** All Rights Reserved.
**                       
** Distributed under the BSD Software License (see license.txt)  
***/

public class WavpackContext
{
	internal WavpackConfig config = new WavpackConfig();
	internal WavpackStream stream = new WavpackStream();
	
	internal byte[] read_buffer = new byte[Defines.BITSTREAM_BUFFER_SIZE];
	internal string error_message;
	
	internal System.IO.BinaryReader infile;
	internal long total_samples, crc_errors; // was uint32_t in C
	internal int open_flags, norm_offset;
	internal int reduced_channels = 0;
	internal int lossy_blocks;
	internal int status = 0; // 0 ok, 1 error

	internal bool five = false;

	// optional = new
	internal eFileFormat file_format = eFileFormat.WP_FORMAT_WAV;
	internal string file_extension = null;
	internal byte[] riff_header;
	//internal byte[] riff_trailer;
}