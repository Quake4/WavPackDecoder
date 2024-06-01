/*

Copyright © 2024 Oleg Samsonov aka Quake4. All rights reserved.
https://github.com/Quake4/WavPackDecoder

This Source Code Form is subject to the terms of the Mozilla
Public License, v. 2.0. If a copy of the MPL was not distributed
with this file, You can obtain one at http://mozilla.org/MPL/2.0/.

*/

using System;

namespace WavPack
{
	static class DsdUtils
	{
		internal static int init_dsd_block(WavpackContext wpc, WavpackMetadata wpmd)
		{
			WavpackStream wps = wpc.stream;

			if (wpmd.byte_length < 2 || wpmd.data[0] > 31)
				return Defines.FALSE;

			if (!wpmd.copy_data())
				return Defines.FALSE;

			wps.dsd = new WavpackStream.dsds()
			{
				//filters = new WavpackStream.DSDfilters[2],
				data = wpmd.data,
			};

			// safe to cast away const on stream 0 only
			if (true/*!wps.stream_index*/)
				wpc.dsd_multiplier = 1U << wps.dsd.data[wps.dsd.byteptr++];
			//else
			//	wps.dsd.byteptr++;

			wps.dsd.mode = wps.dsd.data[wps.dsd.byteptr++];

			if (wps.dsd.mode == 0)
			{
				if (wps.dsd.data.Length - wps.dsd.byteptr != wps.wphdr.block_samples * ((wps.wphdr.flags & Defines.MONO_DATA) > 0 ? 1 : 2))
					return Defines.FALSE;

				wps.dsd.ready = true;
				return Defines.TRUE;
			}
			else if (wps.dsd.mode == 1)
				return init_dsd_block_fast(wps, wpmd);
			//else if (wps.dsd.mode == 3)
			//	return init_dsd_block_high(wps, wpmd);
			else
				return Defines.FALSE;
		}

		internal static long unpack_dsd_samples(WavpackContext wpc, int[] buffer, long sample_count, int bufferStartPos)
		{
			WavpackStream wps = wpc.stream;

			uint flags = wps.wphdr.flags;

			// don't attempt to decode past the end of the block, but watch out for overflow!

			if (wps.sample_index + sample_count > wps.wphdr.block_index + wps.wphdr.block_samples &&
				(wps.wphdr.block_index + wps.wphdr.block_samples - wps.sample_index) < sample_count)
				sample_count = wps.wphdr.block_index + wps.wphdr.block_samples - wps.sample_index;

			if (wps.wphdr.block_index > wps.sample_index || wps.wphdr.block_samples < sample_count)
				wps.mute_error = true;

			if (!wps.mute_error)
			{
				if (wps.dsd.mode == 0)
				{
					long total_samples = sample_count * ((flags & Defines.MONO_DATA) > 0 ? 1 : 2);

					if (wps.dsd.data.Length - wps.dsd.byteptr < total_samples)
						total_samples = wps.dsd.data.Length - wps.dsd.byteptr;

					while (total_samples-- > 0)
						wps.crc += (wps.crc << 1) + (buffer[bufferStartPos++] = wps.dsd.data[wps.dsd.byteptr++]);
				}
				else if (wps.dsd.mode == 1)
				{
					if (decode_fast(wps, buffer, sample_count, bufferStartPos) == 0)
						wps.mute_error = true;
				}
				//else if (wps.dsd.mode == 3)
				//{
				//	if (decode_high(wps, buffer, sample_count, bufferStartPos) == 0)
				//		wps.mute_error = true;
				//}
				else
					wps.mute_error = true;

				// If we just finished this block, then it's time to check if the applicable checksum matches. Like
				// other decoding errors, this is indicated by setting the mute_error flag.

				if (wps.sample_index + sample_count == wps.wphdr.block_index + wps.wphdr.block_samples &&
					!wps.mute_error && wps.crc != wps.wphdr.crc)
					wps.mute_error = true;
			}

			if (wps.mute_error)
			{
				long samples_to_null;
				if (wpc.reduced_channels == 1 || wpc.config.num_channels == 1 || (flags & Defines.MONO_FLAG) > 0)
					samples_to_null = sample_count;
				else
					samples_to_null = sample_count * 2;

				while (samples_to_null > 0)
					buffer[--samples_to_null] = 0x55;

				wps.sample_index += sample_count;
				return sample_count;
			}

			if ((flags & Defines.FALSE_STEREO) > 0)
			{
				int dest_idx = (int)sample_count * 2;
				int src_idx = (int)sample_count;
				int c = (int)sample_count;

				while (c-- > 0)
				{
					src_idx--;
					buffer[--dest_idx + bufferStartPos] = buffer[src_idx + bufferStartPos];
					buffer[--dest_idx + bufferStartPos] = buffer[src_idx + bufferStartPos];
				}
			}

			wps.sample_index += sample_count;

			return sample_count;
		}

		// #define DSD_BYTE_READY(low,high) (((low) >> 24) == ((high) >> 24))
		// #define DSD_BYTE_READY(low,high) (!(((low) ^ (high)) >> 24))
		//#define DSD_BYTE_READY(low,high) (!(((low) ^ (high)) & 0xff000000))

		// maximum number of history bits in DSD "fast" mode note that 5 history bits requires 32 history bins
		const byte MAX_HISTORY_BITS = 5;
		// maximum bytes for the value lookup array (per bin) such that the total
		// storage per bin = 2K (also counting probabilities and summed_probabilities)
		const int MAX_BYTES_PER_BIN = 1280;
		const int MAX_DSD_BITS_VALUE = 256;
		
		static int init_dsd_block_fast(WavpackStream wps, WavpackMetadata wpmd)
		{
			byte max_probability;
			int total_summed_probabilities = 0, bi, i;

			if (wps.dsd.byteptr == wps.dsd.data.Length)
				return Defines.FALSE;

			byte history_bits = wps.dsd.data[wps.dsd.byteptr++];

			if (wps.dsd.byteptr == wps.dsd.data.Length || history_bits > MAX_HISTORY_BITS)
				return Defines.FALSE;

			wps.dsd.history_bins = 1 << history_bits;

			wps.dsd.lookup_buffer = new byte[wps.dsd.history_bins * MAX_BYTES_PER_BIN];
			wps.dsd.value_lookup = new int[wps.dsd.history_bins];
			wps.dsd.summed_probabilities = new ushort[MAX_DSD_BITS_VALUE * wps.dsd.history_bins];
			wps.dsd.probabilities = new byte[MAX_DSD_BITS_VALUE * wps.dsd.history_bins];

			max_probability = wps.dsd.data[wps.dsd.byteptr++];

			if (max_probability < 0xff)
			{
				int outptr = 0;
				int outend = wps.dsd.probabilities.Length;

				while (outptr < outend && wps.dsd.byteptr < wps.dsd.data.Length)
				{
					byte code = wps.dsd.data[wps.dsd.byteptr++];

					if (code > max_probability)
					{
						int zcount = code - max_probability;

						while (outptr < outend && zcount-- > 0)
							wps.dsd.probabilities[outptr++] = 0;
					}
					else if (code != 0)
						wps.dsd.probabilities[outptr++] = code;
					else
						break;
				}

				if (outptr < outend || wps.dsd.byteptr < wps.dsd.data.Length && wps.dsd.data[wps.dsd.byteptr++] > 0)
					return Defines.FALSE;
			}
			else if (wps.dsd.data.Length - wps.dsd.byteptr > wps.dsd.probabilities.Length)
			{
				Array.Copy(wps.dsd.data, wps.dsd.byteptr, wps.dsd.probabilities, 0, wps.dsd.probabilities.Length);
				wps.dsd.byteptr += wps.dsd.probabilities.Length;
			}
			else
				return Defines.FALSE;

			int lb_ptr = 0;

			for (bi = 0; bi < wps.dsd.history_bins; ++bi)
			{
				ushort sum_values;
				var bi_index = bi * MAX_DSD_BITS_VALUE;

				for (sum_values = 0, i = 0; i < MAX_DSD_BITS_VALUE; ++i)
					wps.dsd.summed_probabilities[bi_index + i] = sum_values += wps.dsd.probabilities[bi_index + i];

				if (sum_values != 0)
				{
					if ((total_summed_probabilities += sum_values) > wps.dsd.history_bins * MAX_BYTES_PER_BIN)
						return Defines.FALSE;

					wps.dsd.value_lookup[bi] = lb_ptr;

					for (i = 0; i < MAX_DSD_BITS_VALUE; i++)
					{
						int c = wps.dsd.probabilities[bi_index + i];

						while (c-- > 0)
							wps.dsd.lookup_buffer[lb_ptr++] = (byte)i;
					}
				}
			}

			if (wps.dsd.data.Length - wps.dsd.byteptr < 4 || total_summed_probabilities > wps.dsd.history_bins * MAX_BYTES_PER_BIN)
				return Defines.FALSE;

			for (i = 4; i > 0 ; i--)
				wps.dsd.value = (wps.dsd.value << 8) | wps.dsd.data[wps.dsd.byteptr++];

			wps.dsd.p0 = wps.dsd.p1 = 0;
			wps.dsd.low = 0; wps.dsd.high = 0xffffffff;
			wps.dsd.ready = true;

			return Defines.TRUE;
		}
		
		static long decode_fast(WavpackStream wps, int[] output, long sample_count, int bufferStartPos)
		{
			long total_samples = sample_count;
			
			if ((wps.wphdr.flags & Defines.MONO_DATA) == 0)
				total_samples *= 2;

			while (total_samples-- > 0)
			{
				uint mult, index, i;
				int code;

				if (wps.dsd.summed_probabilities[wps.dsd.p0 * MAX_DSD_BITS_VALUE + 255] == 0)
					return 0;

				mult = (wps.dsd.high - wps.dsd.low) / wps.dsd.summed_probabilities[wps.dsd.p0 * MAX_DSD_BITS_VALUE + 255];

				if (mult == 0)
				{
					if (wps.dsd.data.Length - wps.dsd.byteptr >= 4)
						for (i = 4; i > 0; i--)
							wps.dsd.value = (wps.dsd.value << 8) | wps.dsd.data[wps.dsd.byteptr++];

					wps.dsd.low = 0;
					wps.dsd.high = 0xffffffff;
					mult = wps.dsd.high / wps.dsd.summed_probabilities[wps.dsd.p0 * MAX_DSD_BITS_VALUE + 255];

					if (mult == 0)
						return 0;
				}

				index = (wps.dsd.value - wps.dsd.low) / mult;

				if (index >= wps.dsd.summed_probabilities[wps.dsd.p0 * MAX_DSD_BITS_VALUE + 255])
					return 0;

				if ((output[bufferStartPos++] = code = wps.dsd.lookup_buffer[wps.dsd.value_lookup[wps.dsd.p0] + index]) > 0)
					wps.dsd.low += wps.dsd.summed_probabilities[wps.dsd.p0 * MAX_DSD_BITS_VALUE + code - 1] * mult;

				wps.dsd.high = wps.dsd.low + wps.dsd.probabilities[wps.dsd.p0 * MAX_DSD_BITS_VALUE + code] * mult - 1;
				wps.crc += (wps.crc << 1) + code;

				if ((wps.wphdr.flags & Defines.MONO_DATA) > 0)
					wps.dsd.p0 = code & (wps.dsd.history_bins - 1);
				else
				{
					wps.dsd.p0 = wps.dsd.p1;
					wps.dsd.p1 = code & (wps.dsd.history_bins - 1);
				}

				while (((wps.dsd.high ^ wps.dsd.low) & 0xff000000) == 0 && wps.dsd.byteptr < wps.dsd.data.Length)
				{
					wps.dsd.value = (wps.dsd.value << 8) | wps.dsd.data[wps.dsd.byteptr++];
					wps.dsd.high = (wps.dsd.high << 8) | 0xff;
					wps.dsd.low <<= 8;
				}
			}

			return sample_count;
		}
		/*
		const int PTABLE_BITS = 8;
		const int PTABLE_BINS = (1 << PTABLE_BITS);
		const int PTABLE_MASK = PTABLE_BINS - 1;

		const int UP = 0x010000fe;
		const int DOWN = 0x00010000;
		const int DECAY = 8;

		const int PRECISION = 20;
		const int VALUE_ONE = 1 << PRECISION;
		const int PRECISION_USE = 12;

		const int RATE_S = 20;

		static void init_ptable(int* table, int rate_i, int rate_s)
		{
			int value = 0x808000, rate = rate_i << 8, c, i;

			for (c = (rate + 128) >> 8; c--;)
				value += (DOWN - value) >> DECAY;

			for (i = 0; i < PTABLE_BINS / 2; ++i)
			{
				table[i] = value;
				table[PTABLE_BINS - 1 - i] = 0x100ffff - value;

				if (value > 0x010000)
				{
					rate += (rate * rate_s + 128) >> 8;

					for (c = (rate + 64) >> 7; c--;)
						value += (DOWN - value) >> DECAY;
				}
			}
		}

		static int init_dsd_block_high(WavpackStream* wps, WavpackMetadata* wpmd)
		{
			uint32_t flags = wps.wphdr.flags;
			int channel, rate_i, rate_s, i;

			if (wps.dsd.endptr - wps.dsd.byteptr < ((flags & MONO_DATA) ? 13 : 20))
				return FALSE;

			rate_i = *wps.dsd.byteptr++;
			rate_s = *wps.dsd.byteptr++;

			if (rate_s != RATE_S)
				return FALSE;

			if (!wps.dsd.ptable)
				wps.dsd.ptable = (int32_t*)malloc(PTABLE_BINS * sizeof(*wps.dsd.ptable));

			init_ptable(wps.dsd.ptable, rate_i, rate_s);

			for (channel = 0; channel < ((flags & MONO_DATA) ? 1 : 2); ++channel)
			{
				DSDfilters* sp = wps.dsd.filters + channel;

				sp->filter1 = *wps.dsd.byteptr++ << (PRECISION - 8);
				sp->filter2 = *wps.dsd.byteptr++ << (PRECISION - 8);
				sp->filter3 = *wps.dsd.byteptr++ << (PRECISION - 8);
				sp->filter4 = *wps.dsd.byteptr++ << (PRECISION - 8);
				sp->filter5 = *wps.dsd.byteptr++ << (PRECISION - 8);
				sp->filter6 = 0;
				sp->factor = *wps.dsd.byteptr++ & 0xff;
				sp->factor |= (*wps.dsd.byteptr++ << 8) & 0xff00;
				sp->factor = (int32_t)((uint32_t)sp->factor << 16) >> 16;
			}

			wps.dsd.high = 0xffffffff;
			wps.dsd.low = 0x0;

			for (i = 4; i--;)
				wps.dsd.value = (wps.dsd.value << 8) | *wps.dsd.byteptr++;

			wps.dsd.ready = 1;

			return TRUE;
		}

		static int decode_high(WavpackStream* wps, int32_t* output, int sample_count)
		{
			int total_samples = sample_count, stereo = (wps.wphdr.flags & MONO_DATA) ? 0 : 1;
			DSDfilters* sp = wps.dsd.filters;

			while (total_samples--)
			{
				int bitcount = 8;

				sp[0].value = sp[0].filter1 - sp[0].filter5 + ((sp[0].filter6 * sp[0].factor) >> 2);

				if (stereo)
					sp[1].value = sp[1].filter1 - sp[1].filter5 + ((sp[1].filter6 * sp[1].factor) >> 2);

				while (bitcount--)
				{
					int32_t* pp = wps.dsd.ptable + ((sp[0].value >> (PRECISION - PRECISION_USE)) & PTABLE_MASK);
					uint32_t split = wps.dsd.low + ((wps.dsd.high - wps.dsd.low) >> 8) * (*pp >> 16);

					if (wps.dsd.value <= split)
					{
						wps.dsd.high = split;
						*pp += (UP - *pp) >> DECAY;
						sp[0].filter0 = -1;
					}
					else
					{
						wps.dsd.low = split + 1;
						*pp += (DOWN - *pp) >> DECAY;
						sp[0].filter0 = 0;
					}

					while (DSD_BYTE_READY(wps.dsd.high, wps.dsd.low) && wps.dsd.byteptr < wps.dsd.endptr)
					{
						wps.dsd.value = (wps.dsd.value << 8) | *wps.dsd.byteptr++;
						wps.dsd.high = (wps.dsd.high << 8) | 0xff;
						wps.dsd.low <<= 8;
					}

					sp[0].value += sp[0].filter6 * 8;
					sp[0].byte = (sp[0].byte << 1) | (sp[0].filter0 & 1);
					sp[0].factor += (((sp[0].value ^ sp[0].filter0) >> 31) | 1) & ((sp[0].value ^ (sp[0].value - (sp[0].filter6 * 16))) >> 31);
					sp[0].filter1 += ((sp[0].filter0 & VALUE_ONE) - sp[0].filter1) >> 6;
					sp[0].filter2 += ((sp[0].filter0 & VALUE_ONE) - sp[0].filter2) >> 4;
					sp[0].filter3 += (sp[0].filter2 - sp[0].filter3) >> 4;
					sp[0].filter4 += (sp[0].filter3 - sp[0].filter4) >> 4;
					sp[0].value = (sp[0].filter4 - sp[0].filter5) >> 4;
					sp[0].filter5 += sp[0].value;
					sp[0].filter6 += (sp[0].value - sp[0].filter6) >> 3;
					sp[0].value = sp[0].filter1 - sp[0].filter5 + ((sp[0].filter6 * sp[0].factor) >> 2);

					if (!stereo)
						continue;

					pp = wps.dsd.ptable + ((sp[1].value >> (PRECISION - PRECISION_USE)) & PTABLE_MASK);
					split = wps.dsd.low + ((wps.dsd.high - wps.dsd.low) >> 8) * (*pp >> 16);

					if (wps.dsd.value <= split)
					{
						wps.dsd.high = split;
						*pp += (UP - *pp) >> DECAY;
						sp[1].filter0 = -1;
					}
					else
					{
						wps.dsd.low = split + 1;
						*pp += (DOWN - *pp) >> DECAY;
						sp[1].filter0 = 0;
					}

					while (DSD_BYTE_READY(wps.dsd.high, wps.dsd.low) && wps.dsd.byteptr < wps.dsd.endptr)
					{
						wps.dsd.value = (wps.dsd.value << 8) | *wps.dsd.byteptr++;
						wps.dsd.high = (wps.dsd.high << 8) | 0xff;
						wps.dsd.low <<= 8;
					}

					sp[1].value += sp[1].filter6 * 8;
					sp[1].byte = (sp[1].byte << 1) | (sp[1].filter0 & 1);
					sp[1].factor += (((sp[1].value ^ sp[1].filter0) >> 31) | 1) & ((sp[1].value ^ (sp[1].value - (sp[1].filter6 * 16))) >> 31);
					sp[1].filter1 += ((sp[1].filter0 & VALUE_ONE) - sp[1].filter1) >> 6;
					sp[1].filter2 += ((sp[1].filter0 & VALUE_ONE) - sp[1].filter2) >> 4;
					sp[1].filter3 += (sp[1].filter2 - sp[1].filter3) >> 4;
					sp[1].filter4 += (sp[1].filter3 - sp[1].filter4) >> 4;
					sp[1].value = (sp[1].filter4 - sp[1].filter5) >> 4;
					sp[1].filter5 += sp[1].value;
					sp[1].filter6 += (sp[1].value - sp[1].filter6) >> 3;
					sp[1].value = sp[1].filter1 - sp[1].filter5 + ((sp[1].filter6 * sp[1].factor) >> 2);
				}

				wps.crc += (wps.crc << 1) + (*output++ = sp[0].byte &0xff);
				sp[0].factor -= (sp[0].factor + 512) >> 10;

				if (stereo)
				{
					wps.crc += (wps.crc << 1) + (*output++ = wps.dsd.filters[1].byte &0xff);
					wps.dsd.filters[1].factor -= (wps.dsd.filters[1].factor + 512) >> 10;
				}
			}

			return sample_count;
		}
		*/
	}
}