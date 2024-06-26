/*
** WavpackStream.cs
**
** Copyright (c) 2010-2016 Peter McQuillan
**
** All Rights Reserved.
**                       
** Distributed under the BSD Software License (see license.txt)  
***/

namespace WavPack
{
class WavpackStream
{
	internal class DSDfilters
	{
		internal int value, filter0, filter1, filter2, filter3, filter4, filter5, filter6, factor;
		internal int bytei;
	};

	internal struct dsds
	{
		internal byte[] data;
		internal int byteptr;
		internal byte[] probabilities;//[256]
		internal byte[] lookup_buffer;
		internal int[] value_lookup;
		internal byte mode;
		internal bool ready;
		internal int history_bins, p0, p1;
		internal ushort[] summed_probabilities;//256*bins
		internal uint low, high, value;
		internal DSDfilters[] filters;//2
		internal int[] ptable;
	};

	public WavpackStream()
	{
		InitBlock();
	}

	private void InitBlock()
	{
		decorr_passes = new decorr_pass[] { dp1, dp2, dp3, dp4, dp5, dp6, dp7, dp8, dp9, dp10, dp11, dp12, dp13, dp14, dp15, dp16 };
	}

	internal WavpackHeader wphdr = new WavpackHeader();
	internal Bitstream wvbits = new Bitstream();
	internal Bitstream wvcbits;
	internal Bitstream wvxbits;

	internal words_data w = new words_data();

	internal int num_terms = 0;
	internal bool mute_error;
	internal int crc, crc_x, crc_mvx;
	internal long sample_index; // was uint32_t in C

	internal short int32_sent_bits, int32_zeros, int32_ones, int32_dups; // was uchar in C
	internal short float_flags, float_shift, float_max_exp, float_norm_exp; // was uchar in C
	internal byte int32_max_width;
	internal byte float_min_shifted_zeros, float_max_shifted_ones;

	internal decorr_pass dp1 = new decorr_pass();
	internal decorr_pass dp2 = new decorr_pass();
	internal decorr_pass dp3 = new decorr_pass();
	internal decorr_pass dp4 = new decorr_pass();
	internal decorr_pass dp5 = new decorr_pass();
	internal decorr_pass dp6 = new decorr_pass();
	internal decorr_pass dp7 = new decorr_pass();
	internal decorr_pass dp8 = new decorr_pass();
	internal decorr_pass dp9 = new decorr_pass();
	internal decorr_pass dp10 = new decorr_pass();
	internal decorr_pass dp11 = new decorr_pass();
	internal decorr_pass dp12 = new decorr_pass();
	internal decorr_pass dp13 = new decorr_pass();
	internal decorr_pass dp14 = new decorr_pass();
	internal decorr_pass dp15 = new decorr_pass();
	internal decorr_pass dp16 = new decorr_pass();

	internal decorr_pass[] decorr_passes;

	// DSD
	internal dsds dsd;
}
}