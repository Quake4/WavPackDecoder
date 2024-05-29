/*
** Defines.cs
**
** Copyright (c) 2010-2016 Peter McQuillan
**
** All Rights Reserved.
**                       
** Distributed under the BSD Software License (see license.txt)  
***/

public class Defines
{
	// Change the following value to an even number to reflect the maximum number of samples to be processed
	// per call to WavPackUtils.WavpackUnpackSamples

	public const int SAMPLE_BUFFER_SIZE = 4096;
	
	internal const int BITSTREAM_BUFFER_SIZE = 16 * 1024;
	
	internal const int FALSE = 0;
	internal const int TRUE = 1;

	// or-values for "flags"
	internal const byte OPEN_2CH_MAX = 0x8; // open multichannel as stereo (no downmix)

	internal const int BYTES_STORED = 3; // 1-4 bytes/sample
	internal const int MONO_FLAG = 4; // not stereo
	internal const int HYBRID_FLAG = 8; // hybrid mode
	internal const int FALSE_STEREO = 0x40000000; // block is stereo, but data is mono
	
	internal const int SHIFT_LSB = 13;
	internal const long SHIFT_MASK = (0x1fL << SHIFT_LSB);
	
	internal const int FLOAT_DATA = 0x80; // ieee 32-bit floating point data
	
	internal const int SRATE_LSB = 23;
	internal const long SRATE_MASK = (0xfL << SRATE_LSB);
	
	internal const int FINAL_BLOCK = 0x1000; // final block of multichannel segment
	
	
	internal const int MIN_STREAM_VERS = 0x402; // lowest stream version we'll decode
	internal const int MAX_STREAM_VERS = 0x410; // highest stream version we'll decode


	internal const byte ID_UNIQUE = 0x3f;
	internal const byte ID_OPTIONAL_DATA = 0x20;
	internal const byte ID_ODD_SIZE = 0x40;
	internal const byte ID_LARGE = 0x80;

	internal const byte ID_DUMMY = 0x0;
	internal const byte ID_ENCODER_INFO = 0x1;
	internal const byte ID_DECORR_TERMS = 0x2;
	internal const byte ID_DECORR_WEIGHTS = 0x3;
	internal const byte ID_DECORR_SAMPLES = 0x4;
	internal const byte ID_ENTROPY_VARS = 0x5;
	internal const byte ID_HYBRID_PROFILE = 0x6;
	internal const byte ID_SHAPING_WEIGHTS = 0x7;
	internal const byte ID_FLOAT_INFO = 0x8;
	internal const byte ID_INT32_INFO = 0x9;
	internal const byte ID_WV_BITSTREAM = 0xa;
	internal const byte ID_WVC_BITSTREAM = 0xb;
	internal const byte ID_WVX_BITSTREAM = 0xc;
	internal const byte ID_CHANNEL_INFO = 0xd;
	internal const byte ID_DSD_BLOCK = 0xe;

	internal const byte ID_RIFF_HEADER = ID_OPTIONAL_DATA | 0x1;
	internal const byte ID_RIFF_TRAILER = ID_OPTIONAL_DATA | 0x2;
	internal const byte ID_ALT_HEADER = ID_OPTIONAL_DATA | 0x3;
	internal const byte ID_ALT_TRAILER = ID_OPTIONAL_DATA | 0x4;
	internal const byte ID_CONFIG_BLOCK = ID_OPTIONAL_DATA | 0x5;
	internal const byte ID_MD5_CHECKSUM = ID_OPTIONAL_DATA | 0x6;
	internal const byte ID_SAMPLE_RATE = ID_OPTIONAL_DATA | 0x7;
	internal const byte ID_ALT_EXTENSION = ID_OPTIONAL_DATA | 0x8;
	//internal const byte ID_ALT_MD5_CHECKSUM = ID_OPTIONAL_DATA | 0x9;
	internal const byte ID_NEW_CONFIG_BLOCK = ID_OPTIONAL_DATA | 0xa;
	//internal const byte ID_CHANNEL_IDENTITIES = ID_OPTIONAL_DATA | 0xb;
	internal const byte ID_WVX_NEW_BITSTREAM = ID_OPTIONAL_DATA | ID_WVX_BITSTREAM;
	internal const byte ID_BLOCK_CHECKSUM = ID_OPTIONAL_DATA | 0xf;


	internal const int JOINT_STEREO = 0x10; // joint stereo
	internal const int CROSS_DECORR = 0x20; // no-delay cross decorrelation
	internal const int HYBRID_SHAPE = 0x40; // noise shape (hybrid mode only)
	
	internal const int INT32_DATA = 0x100; // special extended int handling
	internal const int HYBRID_BITRATE = 0x200; // bitrate noise (hybrid mode only)
	internal const int HYBRID_BALANCE = 0x400; // balance noise (hybrid stereo mode only)
	
	internal const int INITIAL_BLOCK = 0x800; // initial block of multichannel segment
	
	internal const int FLOAT_SHIFT_ONES = 1; // bits left-shifted into float = '1'
	internal const int FLOAT_SHIFT_SAME = 2; // bits left-shifted into float are the same
	internal const int FLOAT_SHIFT_SENT = 4; // bits shifted into float are sent literally
	internal const int FLOAT_ZEROS_SENT = 8; // "zeros" are not all real zeros
	internal const int FLOAT_NEG_ZEROS = 0x10; // contains negative zeros
	internal const int FLOAT_EXCEPTIONS = 0x20; // contains exceptions (inf, nan, etc.)


	internal const int MAX_NTERMS = 16;
	internal const int MAX_TERM = 8;
	
	internal const int MAG_LSB = 18;
	internal const long MAG_MASK = 0x1fL << MAG_LSB;


	internal const long CONFIG_BYTES_STORED = 3; // 1-4 bytes/sample
	internal const long CONFIG_MONO_FLAG = 4; // not stereo
	internal const long CONFIG_HYBRID_FLAG = 8; // hybrid mode
	internal const long CONFIG_JOINT_STEREO = 0x10; // joint stereo
	internal const long CONFIG_CROSS_DECORR = 0x20; // no-delay cross decorrelation
	internal const long CONFIG_HYBRID_SHAPE = 0x40; // noise shape (hybrid mode only)
	internal const long CONFIG_FLOAT_DATA = 0x80; // ieee 32-bit floating point data
	internal const long CONFIG_FAST_FLAG = 0x200; // fast mode
	internal const long CONFIG_HIGH_FLAG = 0x800; // high quality mode
	internal const long CONFIG_VERY_HIGH_FLAG = 0x1000; // very high
	internal const long CONFIG_BITRATE_KBPS = 0x2000; // bitrate is kbps, not bits / sample
	internal const long CONFIG_AUTO_SHAPING = 0x4000; // automatic noise shaping
	internal const long CONFIG_SHAPE_OVERRIDE = 0x8000; // shaping mode specified
	internal const long CONFIG_JOINT_OVERRIDE = 0x10000; // joint-stereo mode specified
	internal const long CONFIG_CREATE_EXE = 0x40000; // create executable
	internal const long CONFIG_CREATE_WVC = 0x80000; // create correction file
	internal const long CONFIG_OPTIMIZE_WVC = 0x100000; // maximize bybrid compression
	internal const long CONFIG_CALC_NOISE = 0x800000; // calc noise in hybrid mode
	internal const long CONFIG_LOSSY_MODE = 0x1000000; // obsolete (for information)
	internal const long CONFIG_EXTRA_MODE = 0x2000000; // extra processing mode
	internal const long CONFIG_SKIP_WVX = 0x4000000; // no wvx stream w/ floats & big ints
	internal const long CONFIG_MD5_CHECKSUM = 0x8000000; // compute & store MD5 signature
	internal const long CONFIG_OPTIMIZE_MONO = 0x80000000; // optimize for mono streams posing as stereo
	
	internal const int MODE_WVC = 0x1;
	internal const int MODE_LOSSLESS = 0x2;
	internal const int MODE_HYBRID = 0x4;
	internal const int MODE_FLOAT = 0x8;
	internal const int MODE_VALID_TAG = 0x10;
	internal const int MODE_HIGH = 0x20;
	internal const int MODE_FAST = 0x40;
}

public enum eFileFormat
{
	WAV = 0,       // Microsoft RIFF, including BWF and RF64 variants
	W64 = 1,       // Sony Wave64
	CAF = 2,       // Apple CoreAudio
	DFF = 3,       // Philips DSDIFF
	DSF = 4,       // Sony DSD Format
	AIF = 5,       // Apple AIFF
}