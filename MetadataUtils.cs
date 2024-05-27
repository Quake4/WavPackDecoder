/*
** MetadataUtils.cs
**
** Copyright (c) 2010-2016 Peter McQuillan
**
** All Rights Reserved.
**                       
** Distributed under the BSD Software License (see license.txt)  
***/

class MetadataUtils
{
	internal static int read_metadata_buff(WavpackContext wpc, WavpackMetadata wpmd)
	{
		byte tchar;
		
		if (wpmd.bytecount >= wpc.stream.wphdr.ckSize)
		{
			// we have read all the data in this block
			return Defines.FALSE;
		}
		
		try
		{
			wpmd.id = wpc.infile.ReadByte();
			tchar = wpc.infile.ReadByte();
		}
		catch (System.Exception)
		{
			wpmd.status = 1;
			return Defines.FALSE;
		}
		
		wpmd.bytecount += 2;
		
		wpmd.byte_length = tchar << 1;
		
		if ((wpmd.id & Defines.ID_LARGE) != 0)
		{
			wpmd.id &= unchecked((byte) ~Defines.ID_LARGE);
			
			try
			{
				tchar = wpc.infile.ReadByte();
			}
			catch (System.Exception)
			{
				wpmd.status = 1;
				return Defines.FALSE;
			}
			
			wpmd.byte_length += tchar << 9;
			
			try
			{
				tchar = wpc.infile.ReadByte();
			}
			catch (System.Exception)
			{
				wpmd.status = 1;
				return Defines.FALSE;
			}
			
			wpmd.byte_length += tchar << 17;
			wpmd.bytecount += 2;
		}

		var bytes_to_read = wpmd.byte_length;

		if ((wpmd.id & Defines.ID_ODD_SIZE) != 0)
		{
			wpmd.id &= unchecked((byte)~ Defines.ID_ODD_SIZE);
			wpmd.byte_length--;
		}
		
		if (wpmd.byte_length == 0)
		{
			wpmd.hasdata = false;
			return Defines.TRUE;
		}
		
		wpmd.bytecount += bytes_to_read;
		
		if (bytes_to_read > 0)
		{
			wpmd.data = wpc.read_buffer;

			if (bytes_to_read > wpmd.data.Length)
				wpmd.data = new byte[bytes_to_read];

			try
			{
				if (wpc.infile.BaseStream.Read(wpmd.data, 0, bytes_to_read) != bytes_to_read)
				{
					wpmd.hasdata = false;
					return Defines.FALSE;
				}

				wpmd.hasdata = true;
			}
			catch (System.Exception)
			{
				wpmd.hasdata = false;
				return Defines.FALSE;
			}
		}
		
		return Defines.TRUE;
	}
	
	internal static int process_metadata(WavpackContext wpc, WavpackMetadata wpmd)
	{
		WavpackStream wps = wpc.stream;
		
		switch (wpmd.id)
		{
			case Defines.ID_DUMMY:
				return Defines.TRUE;

			case Defines.ID_DECORR_TERMS:
				return UnpackUtils.read_decorr_terms(wps, wpmd);
			
			case Defines.ID_DECORR_WEIGHTS:
				return UnpackUtils.read_decorr_weights(wps, wpmd);
			
			case Defines.ID_DECORR_SAMPLES:
				return UnpackUtils.read_decorr_samples(wps, wpmd);
			
			case Defines.ID_ENTROPY_VARS:
				return WordsUtils.read_entropy_vars(wps, wpmd);
			
			case Defines.ID_HYBRID_PROFILE:
				return WordsUtils.read_hybrid_profile(wps, wpmd);

			case Defines.ID_SHAPING_WEIGHTS:
				return Defines.TRUE;

			case Defines.ID_FLOAT_INFO:
				return FloatUtils.read_float_info(wps, wpmd);
			
			case Defines.ID_INT32_INFO:
				return UnpackUtils.read_int32_info(wps, wpmd);
			
			case Defines.ID_CHANNEL_INFO:
				return UnpackUtils.read_channel_info(wpc, wpmd);

			case Defines.ID_CONFIG_BLOCK:
				return UnpackUtils.read_config_info(wpc, wpmd);

			case Defines.ID_SAMPLE_RATE:
				return UnpackUtils.read_sample_rate(wpc, wpmd);
		
			case Defines.ID_WV_BITSTREAM:
				return UnpackUtils.init_wv_bitstream(wpc, wpmd);
			
			case Defines.ID_WVC_BITSTREAM:
				return UnpackUtils.init_wvc_bitstream(wpc, wpmd);

			case Defines.ID_WVX_BITSTREAM:
			case Defines.ID_WVX_NEW_BITSTREAM:
				return UnpackUtils.init_wvx_bitstream(wpc, wpmd);

			case Defines.ID_DSD_BLOCK:
				//???
				return Defines.TRUE;


			// ID_OPTIONAL_DATA
			case Defines.ID_NEW_CONFIG_BLOCK:
				return UnpackUtils.read_new_config_info(wpc, wpmd);

			case Defines.ID_RIFF_HEADER:
			case Defines.ID_ALT_HEADER:
				return UnpackUtils.read_header(wpc, wpmd);

			case Defines.ID_RIFF_TRAILER:
			case Defines.ID_ALT_TRAILER:
				return UnpackUtils.read_trailer(wpc, wpmd);

			case Defines.ID_ALT_EXTENSION:
				wpc.file_extension = System.Text.Encoding.UTF8.GetString(wpmd.data, 0, wpmd.byte_length);
				return Defines.TRUE;

			case Defines.ID_BLOCK_CHECKSUM:
				wpc.five = true;
				return Defines.TRUE;

			default:
				{
					if ((wpmd.id & Defines.ID_OPTIONAL_DATA) != 0)
					{
						return Defines.TRUE;
					}
					else
					{
						return Defines.FALSE;
					}
				}
		}
	}
}