using System;
/*
** UnpackUtils.cs
**
** Copyright (c) 2010-2016 Peter McQuillan
**
** All Rights Reserved.
**                       
** Distributed under the BSD Software License (see license.txt)  
***/

class UnpackUtils
{
	///////////////////////////// executable code ////////////////////////////////
	
	// This function initializes everything required to unpack a WavPack block
	// and must be called before unpack_samples() is called to obtain audio data.
	// It is assumed that the WavpackHeader has been read into the wps.wphdr
	// (in the current WavpackStream). This is where all the metadata blocks are
	// scanned up to the one containing the audio bitstream.
	
	internal static int unpack_init(WavpackContext wpc)
	{
		WavpackStream wps = wpc.stream;
		WavpackMetadata wpmd = new WavpackMetadata();
		
		if (wps.wphdr.block_samples > 0 && wps.wphdr.block_index != 0xFFFFFFFF)
			wps.sample_index = wps.wphdr.block_index;
		
		wps.mute_error = 0;
		wps.crc = wps.crc_x = -1;
		wps.wvbits.sr = 0;
		
		while (MetadataUtils.read_metadata_buff(wpc, wpmd) == Defines.TRUE)
		{
			if (MetadataUtils.process_metadata(wpc, wpmd) == Defines.FALSE)
			{
				wpc.error_message = "invalid metadata id " + wpmd.id;
				return Defines.FALSE;
			}
		}

		if (wpmd.bytecount != wpc.stream.wphdr.ckSize)
		{
			wpc.error_message = "invalid reading WavPack metadata block";
			return Defines.FALSE;
		}

		if (wps.wphdr.block_samples != 0 && (wps.wvbits == null || wps.wvbits.end == 0))
		{
			wpc.error_message = "invalid WavPack file";
			return Defines.FALSE;
		}

		if (wps.wphdr.block_samples != 0)
		{
			if ((wps.wphdr.flags & Defines.INT32_DATA) != 0 && wps.int32_sent_bits != 0 && wps.wvxbits == null)
				wpc.lossy_blocks = true;
			
			if ((wps.wphdr.flags & Defines.FLOAT_DATA) != 0 && (wps.float_flags & (Defines.FLOAT_EXCEPTIONS | Defines.FLOAT_ZEROS_SENT | Defines.FLOAT_SHIFT_SENT | Defines.FLOAT_SHIFT_SAME)) != 0)
				wpc.lossy_blocks = true;
		}
		
		wpc.stream = wps;
		return Defines.TRUE;
	}
	

	// This function initialzes the main bitstream for audio samples, which must
	// be in the "wv" file.
	
	internal static int init_wv_bitstream(WavpackContext wpc, WavpackMetadata wpmd)
	{
		WavpackStream wps = wpc.stream;

		if (!wpmd.copy_data())
			return Defines.FALSE;

		//if (wpmd.hasdata)
		wps.wvbits = BitsUtils.bs_open_read(wpmd.data, 0, wpmd.byte_length, wpc.infile, 0, 0);
		/*else if (wpmd.byte_length > 0)
		{
			int len = wpmd.byte_length & 1;
			wps.wvbits = BitsUtils.bs_open_read(wpc.read_buffer, -1, wpc.read_buffer.Length, wpc.infile, wpmd.byte_length + len, 1);
		}*/
		
		return Defines.TRUE;
	}


	// This function initializes the "correction" bitstream for audio samples,
	// which currently must be in the "wvc" file.

	internal static int init_wvc_bitstream(WavpackContext wpc, WavpackMetadata wpmd)
	{
		WavpackStream wps = wpc.stream;

		if ((wpmd.byte_length & 1) > 0 || !wpmd.copy_data())
			return Defines.FALSE;

		wps.wvcbits = BitsUtils.bs_open_read(wpmd.data, 0, wpmd.byte_length, wpc.infile, 0, 0);

		return Defines.TRUE;
	}

	// This function initializes the "extra" bitstream for audio samples which
	// contains the information required to losslessly decompress 32-bit float data
	// or integer data that exceeds 24 bits. This bitstream is in the "wv" file
	// for pure lossless data or the "wvc" file for hybrid lossless. This data
	// would not be used for hybrid lossy mode. There is also a 32-bit CRC stored
	// in the first 4 bytes of these blocks.

	internal static int init_wvx_bitstream(WavpackContext wpc, WavpackMetadata wpmd)
	{
		WavpackStream wps = wpc.stream;

		int counter = 0;

		if (wpmd.byte_length <= 4 || (wpmd.byte_length & 1) > 0 || !wpmd.copy_data())
			return Defines.FALSE;

		var cp = wpmd.data[counter++];
		wps.crc_mvx = cp;
		wps.crc_mvx |= wpmd.data[counter++] << 8;
		wps.crc_mvx |= wpmd.data[counter++] << 16;
		wps.crc_mvx |= wpmd.data[counter++] << 24;

		wps.wvxbits = BitsUtils.bs_open_read(wpmd.data, counter, wpmd.byte_length, wpc.infile, 0, 0);

		// the new WVX bitstream format starts with one or two new 5-bit fields
		if (wpmd.id == Defines.ID_WVX_NEW_BITSTREAM)
		{
			if ((wps.wphdr.flags & Defines.FLOAT_DATA) > 0)
			{
				wps.float_min_shifted_zeros = (byte)(BitsUtils.getbits(5, wps.wvxbits) & 0x1f);
				wps.float_max_shifted_ones = (byte)(BitsUtils.getbits(5, wps.wvxbits) & 0x1f);
			}
			else
			{
				wps.int32_max_width = (byte)(BitsUtils.getbits(5, wps.wvxbits) & 0x1f);
			}
		}

		return Defines.TRUE;
	}

	// Read decorrelation terms from specified metadata block into the
	// decorr_passes array. The terms range from -3 to 8, plus 17 & 18;
	// other values are reserved and generate errors for now. The delta
	// ranges from 0 to 7 with all values valid. Note that the terms are
	// stored in the opposite order in the decorr_passes array compared
	// to packing.

	internal static int read_decorr_terms(WavpackStream wps, WavpackMetadata wpmd)
	{
		int termcnt = wpmd.byte_length;
		byte[] byteptr = wpmd.data;
		WavpackStream tmpwps = new WavpackStream();
		
		int counter = 0;
		int dcounter = 0;
		
		if (termcnt > Defines.MAX_NTERMS)
			return Defines.FALSE;
		
		tmpwps.num_terms = termcnt;
		
		dcounter = termcnt - 1;
		
		for (dcounter = termcnt - 1; dcounter >= 0; dcounter--)
		{
			tmpwps.decorr_passes[dcounter].term = (short) ((int) (byteptr[counter] & 0x1f) - 5);
			tmpwps.decorr_passes[dcounter].delta = (short) ((byteptr[counter] >> 5) & 0x7);
			
			counter++;
			
			if (tmpwps.decorr_passes[dcounter].term < - 3 || (tmpwps.decorr_passes[dcounter].term > Defines.MAX_TERM && tmpwps.decorr_passes[dcounter].term < 17) || tmpwps.decorr_passes[dcounter].term > 18)
				return Defines.FALSE;
		}
		
		wps.decorr_passes = tmpwps.decorr_passes;
		wps.num_terms = tmpwps.num_terms;
		
		return Defines.TRUE;
	}
	
	
	// Read decorrelation weights from specified metadata block into the
	// decorr_passes array. The weights range +/-1024, but are rounded and
	// truncated to fit in signed chars for metadata storage. Weights are
	// separate for the two channels and are specified from the "last" term
	// (first during encode). Unspecified weights are set to zero.
	
	internal static int read_decorr_weights(WavpackStream wps, WavpackMetadata wpmd)
	{
		int termcnt = wpmd.byte_length, tcount;
		byte[] byteptr = wpmd.data;
		decorr_pass dpp = new decorr_pass();
		int counter = 0;
		int dpp_idx;
		int myiterator = 0;
		
		if ((wps.wphdr.flags & (Defines.MONO_FLAG | Defines.FALSE_STEREO)) == 0)
			termcnt /= 2;
		
		if (termcnt > wps.num_terms)
		{
			return Defines.FALSE;
		}
		
		for (tcount = wps.num_terms; tcount > 0; tcount--)
			dpp.weight_A = dpp.weight_B = 0;
		
		myiterator = wps.num_terms;
		
		while (termcnt > 0)
		{
			dpp_idx = myiterator - 1;
			dpp.weight_A = (short) WordsUtils.restore_weight((sbyte) byteptr[counter]);
			
			wps.decorr_passes[dpp_idx].weight_A = dpp.weight_A;
			
			counter++;
			
			if ((wps.wphdr.flags & (Defines.MONO_FLAG | Defines.FALSE_STEREO)) == 0)
			{
				dpp.weight_B = (short) WordsUtils.restore_weight((sbyte) byteptr[counter]);
				counter++;
			}
			wps.decorr_passes[dpp_idx].weight_B = dpp.weight_B;
			
			myiterator--;
			termcnt--;
		}
		
		return Defines.TRUE;
	}
	
	
	// Read decorrelation samples from specified metadata block into the
	// decorr_passes array. The samples are signed 32-bit values, but are
	// converted to signed log2 values for storage in metadata. Values are
	// stored for both channels and are specified from the "last" term
	// (first during encode) with unspecified samples set to zero. The
	// number of samples stored varies with the actual term value, so
	// those must obviously come first in the metadata.
	
	internal static int read_decorr_samples(WavpackStream wps, WavpackMetadata wpmd)
	{
		byte[] byteptr = wpmd.data;
		decorr_pass dpp = new decorr_pass();
		int tcount;
		int counter = 0;
		int dpp_index = 0;
		int uns_buf0, uns_buf1, uns_buf2, uns_buf3;
		int sample_counter = 0;
		
		dpp_index = 0;
		
		for (tcount = wps.num_terms; tcount > 0; tcount--)
		{
			dpp.term = wps.decorr_passes[dpp_index].term;
			
			for (int internalc = 0; internalc < Defines.MAX_TERM; internalc++)
			{
				dpp.samples_A[internalc] = 0;
				dpp.samples_B[internalc] = 0;
				wps.decorr_passes[dpp_index].samples_A[internalc] = 0;
				wps.decorr_passes[dpp_index].samples_B[internalc] = 0;
			}
			
			dpp_index++;
		}
		
		if (wps.wphdr.version == 0x402 && (wps.wphdr.flags & Defines.HYBRID_FLAG) > 0)
		{
			counter += 2;
			
			if ((wps.wphdr.flags & (Defines.MONO_FLAG | Defines.FALSE_STEREO)) == 0)
				counter += 2;
		}
		
		dpp_index--;
		
		while (counter < wpmd.byte_length)
		{
			if (dpp.term > Defines.MAX_TERM)
			{
				uns_buf0 = byteptr[counter];
				uns_buf1 = byteptr[counter + 1];
				uns_buf2 = byteptr[counter + 2];
				uns_buf3 = byteptr[counter + 3];
				
				dpp.samples_A[0] = WordsUtils.exp2s((short) (uns_buf0 + (uns_buf1 << 8)));
				dpp.samples_A[1] = WordsUtils.exp2s((short) (uns_buf2 + (uns_buf3 << 8)));
				counter += 4;
				
				if ((wps.wphdr.flags & (Defines.MONO_FLAG | Defines.FALSE_STEREO)) == 0)
				{
					
					uns_buf0 = byteptr[counter];
					uns_buf1 = byteptr[counter + 1];
					uns_buf2 = byteptr[counter + 2];
					uns_buf3 = byteptr[counter + 3];
					
					dpp.samples_B[0] = WordsUtils.exp2s((short) (uns_buf0 + (uns_buf1 << 8)));
					dpp.samples_B[1] = WordsUtils.exp2s((short) (uns_buf2 + (uns_buf3 << 8)));
					counter += 4;
				}
			}
			else if (dpp.term < 0)
			{
				uns_buf0 = byteptr[counter];
				uns_buf1 = byteptr[counter + 1];
				uns_buf2 = byteptr[counter + 2];
				uns_buf3 = byteptr[counter + 3];
				
				dpp.samples_A[0] = WordsUtils.exp2s((short) (uns_buf0 + (uns_buf1 << 8)));
				dpp.samples_B[0] = WordsUtils.exp2s((short) (uns_buf2 + (uns_buf3 << 8)));
				
				counter += 4;
			}
			else
			{
				int m = 0, cnt = dpp.term;
				
				while (cnt > 0)
				{
					uns_buf0 = byteptr[counter];
					uns_buf1 = byteptr[counter + 1];
					
					dpp.samples_A[m] = WordsUtils.exp2s((short) (uns_buf0 + (uns_buf1 << 8)));
					counter += 2;
					
					if ((wps.wphdr.flags & (Defines.MONO_FLAG | Defines.FALSE_STEREO)) == 0)
					{
						uns_buf0 = byteptr[counter];
						uns_buf1 = byteptr[counter + 1];
						dpp.samples_B[m] = WordsUtils.exp2s((short) (uns_buf0 + (uns_buf1 << 8)));
						counter += 2;
					}
					
					m++;
					cnt--;
				}
			}
			
			for (sample_counter = 0; sample_counter < Defines.MAX_TERM; sample_counter++)
			{
				wps.decorr_passes[dpp_index].samples_A[sample_counter] = dpp.samples_A[sample_counter];
				wps.decorr_passes[dpp_index].samples_B[sample_counter] = dpp.samples_B[sample_counter];
			}
			
			dpp_index--;
		}
		
		return Defines.TRUE;
	}
	
	
	// Read the int32 data from the specified metadata into the specified stream.
	// This data is used for integer data that has more than 24 bits of magnitude
	// or, in some cases, used to eliminate redundant bits from any audio stream.
	
	internal static int read_int32_info(WavpackStream wps, WavpackMetadata wpmd)
	{
		int bytecnt = wpmd.byte_length;
		byte[] byteptr = wpmd.data;
		int counter = 0;
		
		if (bytecnt != 4)
			return Defines.FALSE; // should also return 0
		
		wps.int32_sent_bits = byteptr[counter++];
		wps.int32_zeros = byteptr[counter++];
		wps.int32_ones = byteptr[counter++];
		wps.int32_dups = byteptr[counter++];
		
		return Defines.TRUE;
	}
	
	
	// Read multichannel information from metadata. The first byte is the total
	// number of channels and the following bytes represent the channel_mask
	// as described for Microsoft WAVEFORMATEX.
	
	internal static int read_channel_info(WavpackContext wpc, WavpackMetadata wpmd)
	{
		int bytecnt = wpmd.byte_length, shift = 0;
		byte[] byteptr = wpmd.data;
		int counter = 0;
		long mask = 0;
		
		if (bytecnt == 0 || bytecnt > 5)
			return Defines.FALSE;
		
		wpc.config.num_channels = byteptr[counter++];
		
		while (bytecnt >= 0)
		{
			mask |= (long) (byteptr[counter++] << shift);
			shift += 8;
			bytecnt--;
		}
		
		wpc.config.channel_mask = mask;
		return Defines.TRUE;
	}


	// Read configuration information from metadata.

	internal static int read_new_config_info(WavpackContext wpc, WavpackMetadata wpmd)
	{
		int bytecnt = wpmd.byte_length;
		byte[] byteptr = wpmd.data;
		int counter = 0;

		wpc.five = true;

		if (bytecnt >= 1)
			wpc.file_format = (eFileFormat)byteptr[counter++];

		return Defines.TRUE;
	}


	// Read configuration information from metadata.

	internal static int read_config_info(WavpackContext wpc, WavpackMetadata wpmd)
	{
		int bytecnt = wpmd.byte_length;
		byte[] byteptr = wpmd.data;
		int counter = 0;
		
		if (bytecnt >= 3)
		{
			wpc.config.flags &= 0xff;
			wpc.config.flags |= (long) (byteptr[counter++] << 8);
			wpc.config.flags |= (long) (byteptr[counter++] << 16);
			wpc.config.flags |= (long) (byteptr[counter++] << 24);
		}

		if (bytecnt >= 5)
			wpc.five = true;
		
		return Defines.TRUE;
	}
	
	// Read non-standard sampling rate from metadata.
	
	internal static int read_sample_rate(WavpackContext wpc, WavpackMetadata wpmd)
	{
		int bytecnt = wpmd.byte_length;
		byte[] byteptr = wpmd.data;
		int counter = 0;
		
		if (bytecnt == 3)
		{
			wpc.config.sample_rate = byteptr[counter++];
			wpc.config.sample_rate |= (long) (byteptr[counter++] << 8);
			wpc.config.sample_rate |= (long) (byteptr[counter++] << 16);
		}
		
		return Defines.TRUE;
	}

	internal static int read_header(WavpackContext wpc, WavpackMetadata wpmd)
	{
		var bytes = new byte[wpmd.byte_length];
		Array.Copy(wpmd.data, bytes, bytes.Length);
		wpc.header = bytes;

		return Defines.TRUE;
	}

	internal static int read_trailer(WavpackContext wpc, WavpackMetadata wpmd)
	{
		var bytes = new byte[wpmd.byte_length];
		Array.Copy(wpmd.data, bytes, bytes.Length);
		wpc.trailer = bytes;

		return Defines.TRUE;
	}


	// This monster actually unpacks the WavPack bitstream(s) into the specified
	// buffer as 32-bit integers or floats (depending on orignal data). Lossy
	// samples will be clipped to their original limits (i.e. 8-bit samples are
	// clipped to -128/+127) but are still returned in ints. It is up to the
	// caller to potentially reformat this for the final output including any
	// multichannel distribution, block alignment or endian compensation. The
	// function unpack_init() must have been called and the entire WavPack block
	// must still be visible (although wps.blockbuff will not be accessed again).
	// For maximum clarity, the function is broken up into segments that handle
	// various modes. This makes for a few extra infrequent flag checks, but
	// makes the code easier to follow because the nesting does not become so
	// deep. For maximum efficiency, the conversion is isolated to tight loops
	// that handle an entire buffer. The function returns the total number of
	// samples unpacked, which can be less than the number requested if an error
	// occurs or the end of the block is reached.

	internal static long unpack_samples(WavpackContext wpc, int[] buffer, long sample_count, int bufferStartPos)
	{
		WavpackStream wps = wpc.stream;
		long flags = wps.wphdr.flags;
		long i;
		int crc = wps.crc;

		int mute_limit = (int) ((1L << (int) ((flags & Defines.MAG_MASK) >> Defines.MAG_LSB)) + 2);
		decorr_pass dpp;
		int tcount;
		int buffer_counter = 0;
		
		int samples_processed = 0;
		
		if (wps.sample_index + sample_count > wps.wphdr.block_index + wps.wphdr.block_samples)
			sample_count = wps.wphdr.block_index + wps.wphdr.block_samples - wps.sample_index;
		
		if (wps.mute_error > 0)
		{
			
			long tempc;
			
			if ((flags & Defines.MONO_FLAG) > 0)
				tempc = sample_count;
			else
				tempc = 2 * sample_count;
			
			buffer_counter = bufferStartPos;
			while (tempc-- > 0)
				buffer[buffer_counter++] = 0;
			
			wps.sample_index += sample_count;
			
			return sample_count;
		}
		
		if ((flags & Defines.HYBRID_FLAG) > 0)
			mute_limit *= 2;
		
		
		///////////////////// handle version 4 mono data /////////////////////////
		
		if ((flags & (Defines.MONO_FLAG | Defines.FALSE_STEREO)) > 0)
		{
			
			int dpp_index = 0;
			
			i = WordsUtils.get_words(sample_count, flags, wps.w, wps.wvbits, buffer, bufferStartPos);
			
			for (tcount = wps.num_terms; tcount > 0; tcount--, dpp_index++)
			{
				dpp = wps.decorr_passes[dpp_index];
				decorr_mono_pass(dpp, buffer, sample_count, bufferStartPos);
			}
			
			int bf_abs;
			int crclimit = (int)(sample_count + bufferStartPos);
			
			for (int q = bufferStartPos; q < crclimit; q++)
			{
				var bf_i = buffer[q];
				bf_abs = bf_i < 0 ? -bf_i : bf_i;
				
				if (bf_abs > mute_limit)
				{
					i = q;
					break;
				}
				crc = crc * 3 + bf_i;
			}
		}
		//////////////////// handle version 4 stereo data ////////////////////////
		else
		{
			samples_processed = WordsUtils.get_words(sample_count, flags, wps.w, wps.wvbits, buffer, bufferStartPos);
			
			i = samples_processed;
			
			if (sample_count < 16)
			{
				int dpp_index = 0;
				
				for (tcount = wps.num_terms; tcount > 0; tcount--, dpp_index++)
				{
					dpp = wps.decorr_passes[dpp_index];
					decorr_stereo_pass(dpp, buffer, sample_count, bufferStartPos);
				}
			}
			else
			{
				int dpp_index = 0;
				
				for (tcount = wps.num_terms; tcount > 0; tcount--, dpp_index++)
				{
					dpp = wps.decorr_passes[dpp_index];
					decorr_stereo_pass(dpp, buffer, 8, bufferStartPos);
					decorr_stereo_pass_cont(dpp, buffer, sample_count - 8, bufferStartPos + 16);
				}
			}
			
			if ((flags & Defines.JOINT_STEREO) > 0)
			{
				int bf_abs, bf1_abs;
				
				for (buffer_counter = 0; buffer_counter < sample_count * 2; buffer_counter += 2)
				{
					buffer[buffer_counter + bufferStartPos] += (buffer[buffer_counter + 1 + bufferStartPos] -= (buffer[buffer_counter + bufferStartPos] >> 1));
					
					bf_abs = (buffer[buffer_counter + bufferStartPos] < 0?- buffer[buffer_counter + bufferStartPos]:buffer[buffer_counter + bufferStartPos]);
					bf1_abs = (buffer[buffer_counter + 1 + bufferStartPos] < 0?- buffer[buffer_counter + 1 + bufferStartPos]:buffer[buffer_counter + 1 + bufferStartPos]);
					
					if (bf_abs > mute_limit || bf1_abs > mute_limit)
					{
						i = buffer_counter / 2;
						break;
					}
					
					crc = (crc * 3 + buffer[buffer_counter + bufferStartPos]) * 3 + buffer[buffer_counter + 1 + bufferStartPos];
				}
			}
			else
			{
				int bf_abs, bf1_abs;
				
				for (buffer_counter = 0; buffer_counter < sample_count * 2; buffer_counter += 2)
				{
					bf_abs = (buffer[buffer_counter + bufferStartPos] < 0?- buffer[buffer_counter + bufferStartPos]:buffer[buffer_counter + bufferStartPos]);
					bf1_abs = (buffer[buffer_counter + 1 + bufferStartPos] < 0?- buffer[buffer_counter + 1 + bufferStartPos]:buffer[buffer_counter + 1 + bufferStartPos]);
					
					if (bf_abs > mute_limit || bf1_abs > mute_limit)
					{
						i = buffer_counter / 2;
						break;
					}
					
					crc = (crc * 3 + buffer[buffer_counter + bufferStartPos]) * 3 + buffer[buffer_counter + 1 + bufferStartPos];
				}
			}
		}
		
		if (i != sample_count)
		{
			long sc = 0;
			
			if ((flags & Defines.MONO_FLAG) > 0)
				sc = sample_count;
			else
				sc = 2 * sample_count;

			buffer_counter = bufferStartPos;
			while (sc-- > 0)
				buffer[buffer_counter++] = 0;
			
			wps.mute_error = 1;
			i = sample_count;
		}
		
		fixup_samples(wps, buffer, i, bufferStartPos);
		
		if ((flags & Defines.FALSE_STEREO) > 0)
		{
			int dest_idx = (int) i * 2;
			int src_idx = (int) i;
			int c = (int) i;
			
			while (c-- > 0)
			{
				src_idx--;
				buffer[--dest_idx + bufferStartPos] = buffer[src_idx + bufferStartPos];
				buffer[--dest_idx + bufferStartPos] = buffer[src_idx + bufferStartPos];
			}
		}
		
		wps.sample_index += i;
		wps.crc = crc;
		
		return i;
	}
	
	internal static void  decorr_stereo_pass(decorr_pass dpp, int[] buffer, long sample_count, int buf_idx)
	{
		int delta = dpp.delta;
		int weight_A = dpp.weight_A;
		int weight_B = dpp.weight_B;
		int sam_A, sam_B;
		int m, k;
		int bptr_counter = 0;

		switch (dpp.term)
		{
			
			case 17: 
				for (bptr_counter = buf_idx; bptr_counter < buf_idx + sample_count * 2; bptr_counter += 2)
				{
					sam_A = 2 * dpp.samples_A[0] - dpp.samples_A[1];
					dpp.samples_A[1] = dpp.samples_A[0];
					dpp.samples_A[0] = (int) ((weight_A * (long) sam_A + 512) >> 10) + buffer[bptr_counter];
					
					if (sam_A != 0 && buffer[bptr_counter] != 0)
					{
						if ((sam_A ^ buffer[bptr_counter]) < 0)
							weight_A -= delta;
						else
							weight_A += delta;
					}
					
					buffer[bptr_counter] = dpp.samples_A[0];
					
					sam_A = 2 * dpp.samples_B[0] - dpp.samples_B[1];
					dpp.samples_B[1] = dpp.samples_B[0];
					dpp.samples_B[0] = (int) ((weight_B * (long) sam_A + 512) >> 10) + buffer[bptr_counter + 1];
					
					if (sam_A != 0 && buffer[bptr_counter + 1] != 0)
					{
						if ((sam_A ^ buffer[bptr_counter + 1]) < 0)
							weight_B -= delta;
						else
							weight_B += delta;
					}
					
					buffer[bptr_counter + 1] = dpp.samples_B[0];
				}
				
				break;
			
			
			case 18: 
				for (bptr_counter = buf_idx; bptr_counter < buf_idx + sample_count * 2; bptr_counter += 2)
				{
					
					sam_A = (3 * dpp.samples_A[0] - dpp.samples_A[1]) >> 1;
					dpp.samples_A[1] = dpp.samples_A[0];
					dpp.samples_A[0] = (int) ((weight_A * (long) sam_A + 512) >> 10) + buffer[bptr_counter];
					
					if (sam_A != 0 && buffer[bptr_counter] != 0)
					{
						if ((sam_A ^ buffer[bptr_counter]) < 0)
							weight_A -= delta;
						else
							weight_A += delta;
					}
					
					buffer[bptr_counter] = dpp.samples_A[0];
					
					sam_A = (3 * dpp.samples_B[0] - dpp.samples_B[1]) >> 1;
					dpp.samples_B[1] = dpp.samples_B[0];
					dpp.samples_B[0] = (int) ((weight_B * (long) sam_A + 512) >> 10) + buffer[bptr_counter + 1];
					
					if (sam_A != 0 && buffer[bptr_counter + 1] != 0)
					{
						if ((sam_A ^ buffer[bptr_counter + 1]) < 0)
							weight_B -= delta;
						else
							weight_B += delta;
					}
					
					buffer[bptr_counter + 1] = dpp.samples_B[0];
				}
				
				break;
			
			
			case - 1: 
				for (bptr_counter = buf_idx; bptr_counter < buf_idx + sample_count * 2; bptr_counter += 2)
				{
					sam_A = buffer[bptr_counter] + (int) ((weight_A * (long) dpp.samples_A[0] + 512) >> 10);
					
					if ((dpp.samples_A[0] ^ buffer[bptr_counter]) < 0)
					{
						if (dpp.samples_A[0] != 0 && buffer[bptr_counter] != 0 && (weight_A -= delta) < - 1024)
							weight_A = (weight_A < 0) ? -1024 : 1024;
					}
					else
					{
						if (dpp.samples_A[0] != 0 && buffer[bptr_counter] != 0 && (weight_A += delta) > 1024)
							weight_A = (weight_A < 0) ? -1024 : 1024;
					}
					
					buffer[bptr_counter] = sam_A;
					dpp.samples_A[0] = buffer[bptr_counter + 1] + (int) ((weight_B * (long) sam_A + 512) >> 10);
					
					if ((sam_A ^ buffer[bptr_counter + 1]) < 0)
					{
						if (sam_A != 0 && buffer[bptr_counter + 1] != 0 && (weight_B -= delta) < - 1024)
							weight_B = (weight_B < 0) ? -1024 : 1024;
					}
					else
					{
						if (sam_A != 0 && buffer[bptr_counter + 1] != 0 && (weight_B += delta) > 1024)
							weight_B = (weight_B < 0) ? -1024 : 1024;
					}

					buffer[bptr_counter + 1] = dpp.samples_A[0];
				}
				
				break;
			
			
			case - 2: 
				sam_B = 0;
				sam_A = 0;
				
				for (bptr_counter = buf_idx; bptr_counter < buf_idx + sample_count * 2; bptr_counter += 2)
				{
					sam_B = buffer[bptr_counter + 1] + (int) ((weight_B * (long) dpp.samples_B[0] + 512) >> 10);
					
					if ((dpp.samples_B[0] ^ buffer[bptr_counter + 1]) < 0)
					{
						if (dpp.samples_B[0] != 0 && buffer[bptr_counter + 1] != 0 && (weight_B -= delta) < - 1024)
							weight_B = (weight_B < 0) ? -1024 : 1024;
					}
					else
					{
						if (dpp.samples_B[0] != 0 && buffer[bptr_counter + 1] != 0 && (weight_B += delta) > 1024)
							weight_B = (weight_B < 0) ? -1024 : 1024;
					}

					buffer[bptr_counter + 1] = sam_B;
					
					dpp.samples_B[0] = buffer[bptr_counter] + (int) ((weight_A * (long) sam_B + 512) >> 10);
					
					if ((sam_B ^ buffer[bptr_counter]) < 0)
					{
						if (sam_B != 0 && buffer[bptr_counter] != 0 && (weight_A -= delta) < - 1024)
							weight_A = (weight_A < 0) ? -1024 : 1024;
					}
					else
					{
						if (sam_B != 0 && buffer[bptr_counter] != 0 && (weight_A += delta) > 1024)
							weight_A = (weight_A < 0) ? -1024 : 1024;
					}
					buffer[bptr_counter] = dpp.samples_B[0];
				}
				
				break;
			
			
			case - 3: 
				sam_A = 0;
				
				for (bptr_counter = buf_idx; bptr_counter < buf_idx + sample_count * 2; bptr_counter += 2)
				{
					sam_A = buffer[bptr_counter] + (int) ((weight_A * (long) dpp.samples_A[0] + 512) >> 10);
					
					if ((dpp.samples_A[0] ^ buffer[bptr_counter]) < 0)
					{
						if (dpp.samples_A[0] != 0 && buffer[bptr_counter] != 0 && (weight_A -= delta) < - 1024)
							weight_A = (weight_A < 0) ? -1024 : 1024;
					}
					else

					{
						if (dpp.samples_A[0] != 0 && buffer[bptr_counter] != 0 && (weight_A += delta) > 1024)
							weight_A = (weight_A < 0) ? -1024 : 1024;
					}

					sam_B = buffer[bptr_counter + 1] + (int) ((weight_B * (long) dpp.samples_B[0] + 512) >> 10);
					
					if ((dpp.samples_B[0] ^ buffer[bptr_counter + 1]) < 0)
					{
						if (dpp.samples_B[0] != 0 && buffer[bptr_counter + 1] != 0 && (weight_B -= delta) < - 1024)
							weight_B = (weight_B < 0) ? -1024 : 1024;
					}
					else
					{
						if (dpp.samples_B[0] != 0 && buffer[bptr_counter + 1] != 0 && (weight_B += delta) > 1024)
							weight_B = (weight_B < 0) ? -1024 : 1024;
					}

					buffer[bptr_counter] = dpp.samples_B[0] = sam_A;
					buffer[bptr_counter + 1] = dpp.samples_A[0] = sam_B;
				}
				
				break;
			
			
			default: 
				sam_A = 0;
				
				for (m = 0, k = dpp.term & (Defines.MAX_TERM - 1), bptr_counter = buf_idx; bptr_counter < buf_idx + sample_count * 2; bptr_counter += 2)
				{
					sam_A = dpp.samples_A[m];
					dpp.samples_A[k] = (int) ((weight_A * (long) sam_A + 512) >> 10) + buffer[bptr_counter];
					
					if (sam_A != 0 && buffer[bptr_counter] != 0)
					{
						if ((sam_A ^ buffer[bptr_counter]) < 0)
							weight_A -= delta;
						else
							weight_A += delta;
					}
					
					buffer[bptr_counter] = dpp.samples_A[k];
					
					sam_A = dpp.samples_B[m];
					dpp.samples_B[k] = (int) ((weight_B * (long) sam_A + 512) >> 10) + buffer[bptr_counter + 1];
					
					if (sam_A != 0 && buffer[bptr_counter + 1] != 0)
					{
						if ((sam_A ^ buffer[bptr_counter + 1]) < 0)
							weight_B -= delta;
						else
							weight_B += delta;
					}
					
					buffer[bptr_counter + 1] = dpp.samples_B[k];
					
					m = (m + 1) & (Defines.MAX_TERM - 1);
					k = (k + 1) & (Defines.MAX_TERM - 1);
				}
				
				if (m != 0)
				{
					int[] temp_samples = new int[Defines.MAX_TERM];
					
					for (int t = 0; t < dpp.samples_A.Length; t++)
					{
						temp_samples[t] = dpp.samples_A[t];
					}
					
					for (k = 0; k < Defines.MAX_TERM; k++, m++)
						dpp.samples_A[k] = temp_samples[m & (Defines.MAX_TERM - 1)];
					
					Array.Copy(dpp.samples_B, 0, temp_samples, 0, dpp.samples_B.Length);
					
					for (k = 0; k < Defines.MAX_TERM; k++, m++)
						dpp.samples_B[k] = temp_samples[m & (Defines.MAX_TERM - 1)];
				}
				
				break;
			
		}
		
		dpp.weight_A = (short) weight_A;
		dpp.weight_B = (short) weight_B;
	}
	
	internal static void  decorr_stereo_pass_cont(decorr_pass dpp, int[] buffer, long sample_count, int buf_idx)
	{
		int delta = dpp.delta, weight_A = dpp.weight_A, weight_B = dpp.weight_B;
		int tptr;
		int sam_A, sam_B;
		int k, i;
		int buffer_index = buf_idx;
		long end_index = buf_idx + sample_count * 2;

		switch (dpp.term)
		{
			
			case 17: 
				for (buffer_index = buf_idx; buffer_index < end_index; buffer_index += 2)
				{
					sam_A = 2 * buffer[buffer_index - 2] - buffer[buffer_index - 4];
					
					buffer[buffer_index] = (int) ((weight_A * (long) sam_A + 512) >> 10) + (sam_B = buffer[buffer_index]);

					if (sam_A != 0 && sam_B != 0)
						weight_A += (((sam_A ^ sam_B) >> 30) | 1) * delta;

					//update_weight (weight_A, delta, sam_A, sam_B);
					
					sam_A = 2 * buffer[buffer_index - 1] - buffer[buffer_index - 3];

					buffer[buffer_index + 1] = (int) ((weight_B * (long) sam_A + 512) >> 10) + (sam_B = buffer[buffer_index + 1]);
					
					if (sam_A != 0 && sam_B != 0)
						weight_B += (((sam_A ^ sam_B) >> 30) | 1) * delta;
				}
				
				dpp.samples_B[0] = buffer[buffer_index - 1];
				dpp.samples_A[0] = buffer[buffer_index - 2];
				dpp.samples_B[1] = buffer[buffer_index - 3];
				dpp.samples_A[1] = buffer[buffer_index - 4];
				break;
			
			
			case 18: 
				for (buffer_index = buf_idx; buffer_index < end_index; buffer_index += 2)
				{
					sam_A = (3 * buffer[buffer_index - 2] - buffer[buffer_index - 4]) >> 1;

					buffer[buffer_index] = (int) ((weight_A * (long) sam_A + 512) >> 10) + (sam_B = buffer[buffer_index]);

					if (sam_A != 0 && sam_B != 0)
						weight_A += (((sam_A ^ sam_B) >> 30) | 1) * delta;
					
					sam_A = (3 * buffer[buffer_index - 1] - buffer[buffer_index - 3]) >> 1;

					buffer[buffer_index + 1] = (int) ((weight_B * (long) sam_A + 512) >> 10) + (sam_B = buffer[buffer_index + 1]);
					
					if (sam_A != 0 && sam_B != 0)
						weight_B += (((sam_A ^ sam_B) >> 30) | 1) * delta;

				}
				
				dpp.samples_B[0] = buffer[buffer_index - 1];
				dpp.samples_A[0] = buffer[buffer_index - 2];
				dpp.samples_B[1] = buffer[buffer_index - 3];
				dpp.samples_A[1] = buffer[buffer_index - 4];
				break;
			
			
			case - 1: 
				for (buffer_index = buf_idx; buffer_index < end_index; buffer_index += 2)
				{
					buffer[buffer_index] = (int) ((weight_A * (long) buffer[buffer_index - 1] + 512) >> 10) + (sam_A = buffer[buffer_index]);
					
					if ((buffer[buffer_index - 1] ^ sam_A) < 0)
					{
						if (buffer[buffer_index - 1] != 0 && sam_A != 0 && (weight_A -= delta) < - 1024)
							weight_A = (weight_A < 0) ? -1024 : 1024;
					}
					else
					{
						if (buffer[buffer_index - 1] != 0 && sam_A != 0 && (weight_A += delta) > 1024)
							weight_A = (weight_A < 0) ? -1024 : 1024;
					}

					buffer[buffer_index + 1] = (int) ((weight_B * (long) buffer[buffer_index] + 512) >> 10) + (sam_A = buffer[buffer_index + 1]);
					
					if ((buffer[buffer_index] ^ sam_A) < 0)
					{
						if (buffer[buffer_index] != 0 && sam_A != 0 && (weight_B -= delta) < - 1024)
							weight_B = (weight_B < 0) ? -1024 : 1024;
					}
					else
					{
						if (buffer[buffer_index] != 0 && sam_A != 0 && (weight_B += delta) > 1024)
							weight_B = (weight_B < 0) ? -1024 : 1024;
					}
				}
				
				dpp.samples_A[0] = buffer[buffer_index - 1];
				break;
			
			
			case - 2: 
				sam_A = 0;
				sam_B = 0;
				
				for (buffer_index = buf_idx; buffer_index < end_index; buffer_index += 2)
				{
					
					buffer[buffer_index + 1] = (int) ((weight_B * (long) buffer[buffer_index - 2] + 512) >> 10) + (sam_A = buffer[buffer_index + 1]);
					
					if ((buffer[buffer_index - 2] ^ sam_A) < 0)
					{
						if (buffer[buffer_index - 2] != 0 && sam_A != 0 && (weight_B -= delta) < - 1024)
							weight_B = (weight_B < 0) ? -1024 : 1024;
					}
					else
					{
						if (buffer[buffer_index - 2] != 0 && sam_A != 0 && (weight_B += delta) > 1024)
							weight_B = (weight_B < 0) ? -1024 : 1024;
					}

					buffer[buffer_index] = (int) ((weight_A * (long) buffer[buffer_index + 1] + 512) >> 10) + (sam_A = buffer[buffer_index]);
					
					if ((buffer[buffer_index + 1] ^ sam_A) < 0)
					{
						if (buffer[buffer_index + 1] != 0 && sam_A != 0 && (weight_A -= delta) < - 1024)
							weight_A = (weight_A < 0) ? -1024 : 1024;
					}
					else
					{
						if (buffer[buffer_index + 1] != 0 && sam_A != 0 && (weight_A += delta) > 1024)
							weight_A = (weight_A < 0) ? -1024 : 1024;
					}
				}
				
				dpp.samples_B[0] = buffer[buffer_index - 2];
				break;
			
			
			case - 3: 
				for (buffer_index = buf_idx; buffer_index < end_index; buffer_index += 2)
				{
					
					buffer[buffer_index] = (int) ((weight_A * (long) buffer[buffer_index - 1] + 512) >> 10) + (sam_A = buffer[buffer_index]);
					
					if ((buffer[buffer_index - 1] ^ sam_A) < 0)
					{
						if (buffer[buffer_index - 1] != 0 && sam_A != 0 && (weight_A -= delta) < - 1024)
							weight_A = (weight_A < 0) ? -1024 : 1024;
					}
					else
					{
						if (buffer[buffer_index - 1] != 0 && sam_A != 0 && (weight_A += delta) > 1024)
							weight_A = (weight_A < 0) ? -1024 : 1024;
					}
					
					buffer[buffer_index + 1] = (int) ((weight_B * (long) buffer[buffer_index - 2] + 512) >> 10) + (sam_A = buffer[buffer_index + 1]);
					
					if ((buffer[buffer_index - 2] ^ sam_A) < 0)
					{
						if (buffer[buffer_index - 2] != 0 && sam_A != 0 && (weight_B -= delta) < - 1024)
							weight_B = (weight_B < 0) ? -1024 : 1024;
					}
					else
					{
						if (buffer[buffer_index - 2] != 0 && sam_A != 0 && (weight_B += delta) > 1024)
							weight_B = (weight_B < 0) ? -1024 : 1024;
					}
				}
				
				dpp.samples_A[0] = buffer[buffer_index - 1];
				dpp.samples_B[0] = buffer[buffer_index - 2];
				break;
			
			
			default: 
				tptr = buf_idx - (dpp.term * 2);
				
				for (buffer_index = buf_idx; buffer_index < end_index; buffer_index += 2)
				{
					buffer[buffer_index] = (int) ((weight_A * (long) buffer[tptr] + 512) >> 10) + (sam_A = buffer[buffer_index]);
					
					if (buffer[tptr] != 0 && sam_A != 0)
						weight_A += (((buffer[tptr] ^ sam_A) >> 30) | 1) * delta;
					
					buffer[buffer_index + 1] = (int) ((weight_B * (long) buffer[tptr + 1] + 512) >> 10) + (sam_A = buffer[buffer_index + 1]);
					
					if (buffer[tptr + 1] != 0 && sam_A != 0)
						weight_B += (((buffer[tptr + 1] ^ sam_A) >> 30) | 1) * delta;
					
					tptr += 2;
				}
				
				buffer_index--;
				
				for (k = dpp.term - 1, i = 8; i > 0; k--)
				{
					i--;
					dpp.samples_B[k & (Defines.MAX_TERM - 1)] = buffer[buffer_index];
					buffer_index--;
					dpp.samples_A[k & (Defines.MAX_TERM - 1)] = buffer[buffer_index];
					buffer_index--;
				}
				
				break;
			
		}
		
		dpp.weight_A = (short) weight_A;
		dpp.weight_B = (short) weight_B;
	}
	
	internal static void  decorr_mono_pass(decorr_pass dpp, int[] buffer, long sample_count, int buf_idx)
	{
		int delta = dpp.delta, weight_A = dpp.weight_A;
		int sam_A;
		int m, k;
		int bptr_counter = 0;
		
		switch (dpp.term)
		{
			
			case 17: 
				for (bptr_counter = buf_idx; bptr_counter < buf_idx + sample_count; bptr_counter++)
				{
					sam_A = 2 * dpp.samples_A[0] - dpp.samples_A[1];
					dpp.samples_A[1] = dpp.samples_A[0];
					dpp.samples_A[0] = (int) ((weight_A * (long) sam_A + 512) >> 10) + buffer[bptr_counter];
					
					if (sam_A != 0 && buffer[bptr_counter] != 0)
					{
						if ((sam_A ^ buffer[bptr_counter]) < 0)
							weight_A -= delta;
						else
							weight_A += delta;
					}
					buffer[bptr_counter] = dpp.samples_A[0];
				}
				
				break;
			
			
			case 18: 
				for (bptr_counter = buf_idx; bptr_counter < buf_idx + sample_count; bptr_counter++)
				{
					sam_A = (3 * dpp.samples_A[0] - dpp.samples_A[1]) >> 1;
					dpp.samples_A[1] = dpp.samples_A[0];
					dpp.samples_A[0] = (int) ((weight_A * (long) sam_A + 512) >> 10) + buffer[bptr_counter];
					
					if (sam_A != 0 && buffer[bptr_counter] != 0)
					{
						if ((sam_A ^ buffer[bptr_counter]) < 0)
							weight_A -= delta;
						else
							weight_A += delta;
					}
					buffer[bptr_counter] = dpp.samples_A[0];
				}
				
				break;
			
			
			default: 
				for (m = 0, k = dpp.term & (Defines.MAX_TERM - 1), bptr_counter = buf_idx; bptr_counter < buf_idx + sample_count; bptr_counter++)
				{
					sam_A = dpp.samples_A[m];
					dpp.samples_A[k] = (int) ((weight_A * (long) sam_A + 512) >> 10) + buffer[bptr_counter];
					
					if (sam_A != 0 && buffer[bptr_counter] != 0)
					{
						if ((sam_A ^ buffer[bptr_counter]) < 0)
							weight_A -= delta;
						else
							weight_A += delta;
					}
					
					buffer[bptr_counter] = dpp.samples_A[k];
					m = (m + 1) & (Defines.MAX_TERM - 1);
					k = (k + 1) & (Defines.MAX_TERM - 1);
				}
				
				if (m != 0)
				{
					int[] temp_samples = new int[Defines.MAX_TERM];
					
					Array.Copy(dpp.samples_A, 0, temp_samples, 0, dpp.samples_A.Length);
					
					for (k = 0; k < Defines.MAX_TERM; k++, m++)
						dpp.samples_A[k] = temp_samples[m & (Defines.MAX_TERM - 1)];
				}
				
				break;
			
		}
		
		dpp.weight_A = (short) weight_A;
	}
	
	
	// This is a helper function for unpack_samples() that applies several final
	// operations. First, if the data is 32-bit float data, then that conversion
	// is done in the float.c module (whether lossy or lossless) and we return.
	// Otherwise, if the extended integer data applies, then that operation is
	// executed first. If the unpacked data is lossy (and not corrected) then
	// it is clipped and shifted in a single operation. Otherwise, if it's
	// lossless then the last step is to apply the final shift (if any).
	
	internal static void fixup_samples(WavpackStream wps, int[] buffer, long sample_count, int bufferStartPos)
	{
		long flags = wps.wphdr.flags;
		bool lossy_flag = (flags & Defines.HYBRID_FLAG) > 0;// && !wps->block2buff;
		int shift = (int) ((flags & Defines.SHIFT_MASK) >> Defines.SHIFT_LSB);
		
		if ((flags & Defines.FLOAT_DATA) > 0)
		{
			FloatUtils.float_values(wps, buffer, (flags & Defines.MONO_FLAG) > 0 ? sample_count : sample_count * 2, bufferStartPos);
			return;
		}
		
		if ((flags & Defines.INT32_DATA) > 0)
		{
			long count = (flags & Defines.MONO_FLAG) > 0 ? sample_count : sample_count * 2;
			int sent_bits = wps.int32_sent_bits, zeros = wps.int32_zeros;
			int ones = wps.int32_ones, dups = wps.int32_dups;
			uint data, mask = (1U << sent_bits) - 1;
			int buffer_counter = bufferStartPos;

			if (wps.wvxbits != null)
			{
				int max_width = wps.int32_max_width;
				int crc = wps.crc_x;

				while (count-- > 0)
				{
					if (sent_bits > 0)
					{
						if (max_width > 0)
						{
							int pvalue = buffer[buffer_counter] < 0 ? ~buffer[buffer_counter] : buffer[buffer_counter];
							int width = WordsUtils.count_bits(pvalue) + sent_bits;
							int bits_to_read = sent_bits;

							if (width <= max_width || (bits_to_read -= width - max_width) > 0)
							{
								data = (uint)BitsUtils.getbits(bits_to_read, wps.wvxbits) & mask;
								buffer[buffer_counter] = (int)((uint)(buffer[buffer_counter] << bits_to_read) | data) << (sent_bits - bits_to_read);
							}
							else
								buffer[buffer_counter] = buffer[buffer_counter] << sent_bits;
						}
						else
						{
							data = (uint)(BitsUtils.getbits(sent_bits, wps.wvxbits) & mask);
							buffer[buffer_counter] = (int)(((uint)buffer[buffer_counter] << sent_bits) | data);
						}
					}

					if (zeros != 0)
						buffer[buffer_counter] <<= zeros;
					else if (ones != 0)
						buffer[buffer_counter] = ((buffer[buffer_counter] + 1) << ones) - 1;
					else if (dups != 0)
						buffer[buffer_counter] = ((buffer[buffer_counter] + (buffer[buffer_counter] & 1)) << dups) - (buffer[buffer_counter] & 1);

					crc = crc * 9 + (buffer[buffer_counter] & 0xffff) * 3 + ((buffer[buffer_counter] >> 16) & 0xffff);

					buffer_counter++;
				}

				wps.crc_x = crc;
			}
			//if ((flags & Defines.HYBRID_FLAG) == 0 && sent_bits == 0 && (zeros + ones + dups) != 0)
			else if (sent_bits == 0 && (zeros + ones + dups) != 0)
			{
				while (lossy_flag && (flags & Defines.BYTES_STORED) == 3 && shift < 8)
				{
					if (zeros > 0)
						zeros--;
					else if (ones > 0)
						ones--;
					else if (dups > 0)
						dups--;
					else
						break;

					shift++;
				}

				while (count-- > 0)
				{
					if (zeros != 0)
						buffer[buffer_counter] <<= zeros;
					else if (ones != 0)
						buffer[buffer_counter] = ((buffer[buffer_counter] + 1) << ones) - 1;
					else if (dups != 0)
						buffer[buffer_counter] = ((buffer[buffer_counter] + (buffer[buffer_counter] & 1)) << dups) - (buffer[buffer_counter] & 1);

					buffer_counter++;
				}
			}
			else
				shift += zeros + sent_bits + ones + dups;
		}

		shift &= 0x1f;

		if (lossy_flag)
		{
			int min_value, max_value, min_shifted, max_shifted;
			int buffer_counter = bufferStartPos;
			
			switch (flags & Defines.BYTES_STORED)
			{
				case 0: 
					min_shifted = (min_value = - 128 >> shift) << shift;
					max_shifted = (max_value = 127 >> shift) << shift;
					break;
				
				case 1: 
					min_shifted = (min_value = - 32768 >> shift) << shift;
					max_shifted = (max_value = 32767 >> shift) << shift;
					break;
				
				case 2: 
					min_shifted = (min_value = - 8388608 >> shift) << shift;
					max_shifted = (max_value = 8388607 >> shift) << shift;
					break;
				
				case 3: 
				default: 
					min_shifted = (min_value = (int) (0x80000000 >> shift)) << shift;
					max_shifted = (max_value = 0x7FFFFFFF >> shift) << shift;
					break;
				}
			
			if ((flags & Defines.MONO_FLAG) == 0)
				sample_count *= 2;
			
			while (sample_count-- > 0)
			{
				if (buffer[buffer_counter] < min_value)
					buffer[buffer_counter] = min_shifted;
				else if (buffer[buffer_counter] > max_value)
					buffer[buffer_counter] = max_shifted;
				else
					buffer[buffer_counter] <<= shift;
				
				buffer_counter++;
			}
		}
		else if (shift != 0)
		{
			int buffer_counter = bufferStartPos;
			
			if ((flags & Defines.MONO_FLAG) == 0)
				sample_count *= 2;
			
			while (sample_count-- > 0)
				buffer[buffer_counter++] <<= shift;
		}
	}
	
	
	// This function checks the crc value(s) for an unpacked block, returning the
	// number of actual crc errors detected for the block. The block must be
	// completely unpacked before this test is valid. For losslessly unpacked
	// blocks of float or extended integer data the extended crc is also checked.
	// Note that WavPack's crc is not a CCITT approved polynomial algorithm, but
	// is a much simpler method that is virtually as robust for real world data.
	
	internal static bool check_crc_error(WavpackContext wpc)
	{
		WavpackStream wps = wpc.stream;

		return wps.crc != wps.wphdr.crc || wps.wvxbits != null && wps.crc_x != wps.crc_mvx;
	}
}