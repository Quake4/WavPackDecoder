using System;
/*
** WavPackUtils.cs
**
** Copyright (c) 2010-2016 Peter McQuillan
**
** All Rights Reserved.
**                       
** Distributed under the BSD Software License (see license.txt)  
***/

namespace WavPack
{
	public class WavPackUtils
	{
		///////////////////////////// local table storage ////////////////////////////

		internal static long[] sample_rates = new long[] { 6000, 8000, 9600, 11025, 12000, 16000, 22050, 24000, 32000, 44100, 48000, 64000, 88200, 96000, 192000 };

		///////////////////////////// executable code ////////////////////////////////


		// This function reads data from the specified stream in search of a valid
		// WavPack 4.0 audio block. If this fails in 1 megabyte (or an invalid or
		// unsupported WavPack block is encountered) then an appropriate message is
		// copied to "error" and NULL is returned, otherwise a pointer to a
		// WavpackContext structure is returned (which is used to call all other
		// functions in this module). This can be initiated at the beginning of a
		// WavPack file, or anywhere inside a WavPack file. To determine the exact
		// position within the file use WavpackGetSampleIndex().  Also,
		// this function will not handle "correction" files, plays only the first
		// two channels of multi-channel files, and is limited in resolution in some
		// large integer or floating point files (but always provides at least 24 bits
		// of resolution).

		public static WavpackContext WavpackOpenFileInput(System.IO.BinaryReader infile, uint flags = 0)
		{
			WavpackContext wpc = new WavpackContext();
			WavpackStream wps = wpc.stream;

			wpc.infile = infile;
			wpc.total_samples = -1;
			wpc.norm_offset = 0;
			wpc.open_flags = 0;

			// open the source file for reading and store the size
			while (wps.wphdr.block_samples == 0)
			{
				wps.wphdr = read_next_header(wpc.infile, wps.wphdr);

				if (wps.wphdr.error)
				{
					wpc.error_message = "not compatible with this version of WavPack file!";
					return wpc;
				}

				if (wps.wphdr.block_samples > 0 && wps.wphdr.total_samples != 0xFFFFFFFF)
					wpc.total_samples = wps.wphdr.total_samples;

				// lets put the stream back in the context

				wpc.stream = wps;

				if (UnpackUtils.unpack_init(wpc) == Defines.FALSE)
					return wpc;
			} // end of while

			wpc.config.flags = wpc.config.flags & ~0xff;
			wpc.config.flags = wpc.config.flags | (wps.wphdr.flags & 0xff);

			wpc.config.bytes_per_sample = (int)((wps.wphdr.flags & Defines.BYTES_STORED) + 1);
			wpc.config.float_norm_exp = wps.float_norm_exp;

			wpc.config.bits_per_sample = (int)((wpc.config.bytes_per_sample * 8) - ((wps.wphdr.flags & Defines.SHIFT_MASK) >> Defines.SHIFT_LSB));

			if ((wpc.config.flags & Defines.FLOAT_DATA) > 0)
			{
				wpc.config.bytes_per_sample = 3;
				wpc.config.bits_per_sample = 24;
			}

			if (wpc.config.sample_rate == 0)
			{
				if (wps.wphdr.block_samples == 0 || (wps.wphdr.flags & Defines.SRATE_MASK) == Defines.SRATE_MASK)
					wpc.config.sample_rate = 44100;
				else
					wpc.config.sample_rate = sample_rates[(int)((wps.wphdr.flags & Defines.SRATE_MASK) >> Defines.SRATE_LSB)];
			}

			if (wpc.config.num_channels == 0)
			{
				if ((wps.wphdr.flags & Defines.MONO_FLAG) > 0)
					wpc.config.num_channels = 1;
				else
					wpc.config.num_channels = 2;

				wpc.config.channel_mask = 0x5 - wpc.config.num_channels;
			}

			if ((flags & Defines.OPEN_2CH_MAX) > 0 && (wps.wphdr.flags & Defines.FINAL_BLOCK) == 0)
			{
				if ((wps.wphdr.flags & Defines.MONO_FLAG) != 0)
					wpc.reduced_channels = 1;
				else
					wpc.reduced_channels = 2;
			}

			if (wpc.config.num_channels > 2)
			{
				wpc.error_message = "only two channels supported!";
				return wpc;
			}

			return wpc;
		}

		// This function obtains general information about an open file and returns
		// a mask with the following bit values:

		// MODE_LOSSLESS:  file is lossless (pure lossless only)
		// MODE_HYBRID:  file is hybrid mode (lossy part only)
		// MODE_FLOAT:  audio data is 32-bit ieee floating point (but will provided
		//               in 24-bit integers for convenience)
		// MODE_HIGH:  file was created in "high" mode (information only)
		// MODE_FAST:  file was created in "fast" mode (information only)


		public static int WavpackGetMode(WavpackContext wpc)
		{
			int mode = 0;

			if ((wpc.config.flags & Defines.CONFIG_HYBRID_FLAG) != 0)
				mode |= Defines.MODE_HYBRID;
			else if ((wpc.config.flags & Defines.CONFIG_LOSSY_MODE) == 0)
				mode |= Defines.MODE_LOSSLESS;

			if (wpc.lossy_blocks)
				mode &= ~Defines.MODE_LOSSLESS;

			if ((wpc.config.flags & Defines.CONFIG_FLOAT_DATA) != 0)
				mode |= Defines.MODE_FLOAT;

			if ((wpc.config.flags & Defines.CONFIG_HIGH_FLAG) != 0)
				mode |= Defines.MODE_HIGH;

			if ((wpc.config.flags & Defines.CONFIG_FAST_FLAG) != 0)
				mode |= Defines.MODE_FAST;

			return mode;
		}


		// Unpack the specified number of samples from the current file position.
		// Note that "samples" here refers to "complete" samples, which would be
		// 2 longs for stereo files. The audio data is returned right-justified in
		// 32-bit longs in the endian mode native to the executing processor. So,
		// if the original data was 16-bit, then the values returned would be
		// +/-32k. Floating point data will be returned as 24-bit integers (and may
		// also be clipped). The actual number of samples unpacked is returned,
		// which should be equal to the number requested unless the end of fle is
		// encountered or an error occurs.

		public static long WavpackUnpackSamples(WavpackContext wpc, int[] buffer, long samples)
		{
			WavpackStream wps = wpc.stream;
			long samples_unpacked = 0, samples_to_unpack;
			int num_channels = wpc.config.num_channels;
			int bcounter = 0;

			int buf_idx = 0;
			int bytes_returned = 0;

			while (samples > 0)
			{
				if (wps.wphdr.block_samples == 0 || (wps.wphdr.flags & Defines.INITIAL_BLOCK) == 0 || wps.sample_index >= wps.wphdr.block_index + wps.wphdr.block_samples)
				{
					wps.wphdr = read_next_header(wpc.infile, wps.wphdr);

					if (wps.wphdr.error)
						break;

					if (wps.wphdr.block_samples == 0 || wps.sample_index == wps.wphdr.block_index)
						if (UnpackUtils.unpack_init(wpc) == Defines.FALSE)
							break;
				}

				if (wps.wphdr.block_samples == 0 || (wps.wphdr.flags & Defines.INITIAL_BLOCK) == 0 || wps.sample_index >= wps.wphdr.block_index + wps.wphdr.block_samples)
					continue;

				if (wps.sample_index < wps.wphdr.block_index)
				{
					samples_to_unpack = wps.wphdr.block_index - wps.sample_index;

					if (samples_to_unpack > samples)
						samples_to_unpack = samples;

					wps.sample_index += samples_to_unpack;
					samples_unpacked += samples_to_unpack;
					samples -= samples_to_unpack;

					if (wpc.reduced_channels > 0)
						samples_to_unpack *= wpc.reduced_channels;
					else
						samples_to_unpack *= num_channels;

					bcounter = buf_idx;

					while (samples_to_unpack-- > 0)
						buffer[bcounter++] = 0;

					buf_idx = bcounter;

					continue;
				}

				samples_to_unpack = wps.wphdr.block_index + wps.wphdr.block_samples - wps.sample_index;

				if (samples_to_unpack > samples)
					samples_to_unpack = samples;

				UnpackUtils.unpack_samples(wpc, buffer, samples_to_unpack, buf_idx);

				if (wpc.reduced_channels > 0)
					bytes_returned = (int)(samples_to_unpack * wpc.reduced_channels);
				else
					bytes_returned = (int)(samples_to_unpack * num_channels);

				buf_idx += bytes_returned;

				samples_unpacked += samples_to_unpack;
				samples -= samples_to_unpack;

				if (wps.sample_index == wps.wphdr.block_index + wps.wphdr.block_samples)
					if (UnpackUtils.check_crc_error(wpc))
						wpc.crc_errors++;

				if (wps.sample_index == wpc.total_samples)
					break;
			}

			return samples_unpacked;
		}


		// Get total number of samples contained in the WavPack file, or -1 if unknown

		public static long WavpackGetNumSamples(WavpackContext wpc)
		{
			// -1 would mean an unknown number of samples
			return wpc.total_samples;
		}


		// Get the current sample index position, or -1 if unknown

		public static long WavpackGetSampleIndex(WavpackContext wpc)
		{
			return wpc.stream.sample_index;
		}


		// Get the number of errors encountered so far

		public static long WavpackGetNumErrors(WavpackContext wpc)
		{
			return wpc.crc_errors;
		}


		// return if any uncorrected lossy blocks were actually written or read

		public static bool WavpackLossyBlocks(WavpackContext wpc)
		{
			return wpc.lossy_blocks;
		}


		// Returns the sample rate of the specified WavPack file

		public static long WavpackGetSampleRate(WavpackContext wpc)
		{
			if (wpc.config.sample_rate != 0)
				return wpc.config.sample_rate;
			else
				return 44100;
		}


		// Returns the number of channels of the specified WavPack file. Note that
		// this is the actual number of channels contained in the file, but this
		// version can only decode the first two.

		public static int WavpackGetNumChannels(WavpackContext wpc)
		{
			if (wpc.config.num_channels != 0)
				return wpc.config.num_channels;
			else
				return 2;
		}


		// Returns the actual number of valid bits per sample contained in the
		// original file, which may or may not be a multiple of 8. Floating data
		// always has 32 bits, integers may be from 1 to 32 bits each. When this
		// value is not a multiple of 8, then the "extra" bits are located in the
		// LSBs of the results. That is, values are right justified when unpacked
		// into longs, but are left justified in the number of bytes used by the
		// original data.

		public static int WavpackGetBitsPerSample(WavpackContext wpc)
		{
			if (wpc.config.bits_per_sample != 0)
				return wpc.config.bits_per_sample;
			else
				return 16;
		}


		// Returns the number of bytes used for each sample (1 to 4) in the original
		// file. This is required information for the user of this module because the
		// audio data is returned in the LOWER bytes of the long buffer and must be
		// left-shifted 8, 16, or 24 bits if normalized longs are required.

		public static int WavpackGetBytesPerSample(WavpackContext wpc)
		{
			if (wpc.config.bytes_per_sample != 0)
				return wpc.config.bytes_per_sample;
			else
				return 2;
		}


		// This function will return the actual number of channels decoded from the
		// file (which may or may not be less than the actual number of channels, but
		// will always be 1 or 2). Normally, this will be the front left and right
		// channels of a multi-channel file.

		public static int WavpackGetReducedChannels(WavpackContext wpc)
		{
			if (wpc.reduced_channels != 0)
				return wpc.reduced_channels;
			else if (wpc.config.num_channels != 0)
				return wpc.config.num_channels;
			else
				return 2;
		}


		// Return the file format specified in the call to WavpackSetFileInformation()
		// when the file was created. For all files created prior to WavPack 5.0 this
		// will 0 (WP_FORMAT_WAV).

		public static eFileFormat WavpackGetFileFormat(WavpackContext wpc)
		{
			return wpc.file_format;
		}


		// Return a string representing the recommended file extension for the open
		// WavPack file. For all files created prior to WavPack 5.0 this will be "wav",
		// even for raw files with no RIFF into. This string is specified in the
		// call to WavpackSetFileInformation() when the file was created.

		public static string WavpackGetFileExtension(WavpackContext wpc)
		{
			if (wpc.file_extension != null)
				return wpc.file_extension;
			else
				return "wav";
		}

		public static string WavpackGetErrorMessage(WavpackContext wpc)
		{
			return wpc.error_message;
		}

		public static byte[] WavpackGetHeader(WavpackContext wpc)
		{
			return wpc.header;
		}

		public static byte[] WavpackGetTrailer(WavpackContext wpc)
		{
			return wpc.trailer;
		}

		public static bool WavpackGetIsFive(WavpackContext wpc)
		{
			return wpc.five;
		}

		public static short WavpackGetVersion(WavpackContext wpc)
		{
			return wpc.stream.wphdr.version;
		}

		public static bool WavpackGetIsFloat(WavpackContext wpc)
		{
			return (wpc.config.flags & Defines.CONFIG_FLOAT_DATA) > 0;
		}


		// The following seek functionality has not yet been extensively tested

		public static void setTime(WavpackContext wpc, long milliseconds)
		{
			long targetSample = (long)(milliseconds / 1000 * wpc.config.sample_rate);
			try
			{
				seek(wpc, wpc.infile, wpc.infile.BaseStream.Position, targetSample);
			}
			catch (System.IO.IOException)
			{
			}
		}

		public static void setSample(WavpackContext wpc, long sample)
		{
			seek(wpc, wpc.infile, 0, sample);
		}

		// Find the WavPack block that contains the specified sample. If "header_pos"
		// is zero, then no information is assumed except the total number of samples
		// in the file and its size in bytes. If "header_pos" is non-zero then we
		// assume that it is the file position of the valid header image contained in
		// the first stream and we can limit our search to either the portion above
		// or below that point. If a .wvc file is being used, then this must be called
		// for that file also.
		private static void seek(WavpackContext wpc, System.IO.BinaryReader infile, long headerPos, long targetSample)
		{
			try
			{
				WavpackStream wps = wpc.stream;
				long file_pos1 = 0;
				long file_pos2 = wpc.infile.BaseStream.Length;
				long sample_pos1 = 0, sample_pos2 = wpc.total_samples;
				double ratio = 0.96;
				int file_skip = 0;
				if (targetSample >= wpc.total_samples)
					return;
				if (headerPos > 0 && wps.wphdr.block_samples > 0)
				{
					if (wps.wphdr.block_index > targetSample)
					{
						sample_pos2 = wps.wphdr.block_index;
						file_pos2 = headerPos;
					}
					else if (wps.wphdr.block_index + wps.wphdr.block_samples <= targetSample)
					{
						sample_pos1 = wps.wphdr.block_index;
						file_pos1 = headerPos;
					}
					else
						return;
				}
				while (true)
				{
					double bytes_per_sample;
					long seek_pos;
					bytes_per_sample = file_pos2 - file_pos1;
					bytes_per_sample /= sample_pos2 - sample_pos1;
					seek_pos = file_pos1 + (file_skip > 0 ? 32 : 0);
					seek_pos += (long)(bytes_per_sample * (targetSample - sample_pos1) * ratio);
					infile.BaseStream.Seek(seek_pos, 0);

					long temppos = infile.BaseStream.Position;
					wps.wphdr = read_next_header(infile, wps.wphdr);

					if (wps.wphdr.error || seek_pos >= file_pos2)
					{
						if (ratio > 0.0)
						{
							if ((ratio -= 0.24) < 0.0)
								ratio = 0.0;
						}
						else
							return;
					}
					else if (wps.wphdr.block_index > targetSample)
					{
						sample_pos2 = wps.wphdr.block_index;
						file_pos2 = seek_pos;
					}
					else if (wps.wphdr.block_index + wps.wphdr.block_samples <= targetSample)
					{
						if (seek_pos == file_pos1)
							file_skip = 1;
						else
						{
							sample_pos1 = wps.wphdr.block_index;
							file_pos1 = seek_pos;
						}
					}
					else
					{
						int index = (int)(targetSample - wps.wphdr.block_index);
						infile.BaseStream.Seek(seek_pos, 0);
						WavpackContext c = WavpackOpenFileInput(infile);
						wpc.stream = c.stream;
						int[] temp_buf = new int[Defines.SAMPLE_BUFFER_SIZE];
						while (index > 0)
						{
							int toUnpack = Math.Min(index, Defines.SAMPLE_BUFFER_SIZE / WavpackGetReducedChannels(wpc));
							WavpackUnpackSamples(wpc, temp_buf, toUnpack);
							index = index - toUnpack;
						}
						return;
					}
				}
			}
			catch (System.IO.IOException)
			{
			}
		}

		// Read from current file position until a valid 32-byte WavPack 4.0 header is
		// found and read into the specified pointer. If no WavPack header is found within 1 meg,
		// then an error is returned. No additional bytes are read past the header. 

		internal static WavpackHeader read_next_header(System.IO.BinaryReader infile, WavpackHeader wphdr)
		{
			byte[] buffer = new byte[32]; // 32 is the size of a WavPack Header
			byte[] temp = new byte[32];

			long bytes_skipped = 0;
			int bleft = 0; // bytes left in buffer
			int counter = 0;
			int i = 0;

			while (true)
			{
				for (i = 0; i < bleft; i++)
				{
					buffer[i] = buffer[32 - bleft + i];
				}

				counter = 0;

				try
				{
					if (infile.BaseStream.Read(temp, 0, 32 - bleft) != 32 - bleft)
					{
						wphdr.error = true;
						return wphdr;
					}
				}
				catch (System.Exception)
				{
					wphdr.error = true;
					return wphdr;
				}

				for (i = 0; i < 32 - bleft; i++)
				{
					buffer[bleft + i] = temp[i];
				}

				bleft = 32;

				if (buffer[0] == 'w' && buffer[1] == 'v' && buffer[2] == 'p' && buffer[3] == 'k' && (buffer[4] & 1) == 0 && buffer[6] < 16 && buffer[7] == 0 && buffer[9] == 4 && buffer[8] >= (Defines.MIN_STREAM_VERS & 0xff) && buffer[8] <= (Defines.MAX_STREAM_VERS & 0xff))
				{
					wphdr.ckSize = (uint)((buffer[7] << 24) | (buffer[6] << 16) | (buffer[5] << 8) | buffer[4]);
					wphdr.version = (short)((buffer[9] << 8) | buffer[8]);
					wphdr.track_no = buffer[10];
					wphdr.index_no = buffer[11];
					wphdr.total_samples = (uint)((buffer[15] << 24) | (buffer[14] << 16) | (buffer[13] << 8) | buffer[12]);
					wphdr.block_index = (uint)((buffer[19] << 24) | (buffer[18] << 16) | (buffer[17] << 8) | buffer[16]);
					wphdr.block_samples = (uint)((buffer[23] << 24) | (buffer[22] << 16) | (buffer[21] << 8) | buffer[20]);
					wphdr.flags = (uint)((buffer[27] << 24) | (buffer[26] << 16) | (buffer[25] << 8) | buffer[24]);
					wphdr.crc = (buffer[31] << 24) | (buffer[30] << 16) | (buffer[29] << 8) | buffer[28];

					wphdr.error = false;

					return wphdr;
				}
				else
				{
					counter++;
					bleft--;
				}

				while (bleft > 0 && buffer[counter] != 'w')
				{
					counter++;
					bleft--;
				}

				bytes_skipped = bytes_skipped + counter;

				if (bytes_skipped > 1048576L)
				{
					wphdr.error = true;
					return wphdr;
				}
			}
		}
	}
}