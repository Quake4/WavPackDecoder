/*
** BitsUtils.cs
**
** Copyright (c) 2010-2016 Peter McQuillan
**
** All Rights Reserved.
**                       
** Distributed under the BSD Software License (see license.txt)  
***/

namespace WavPack
{
	class BitsUtils
	{
		internal static bool getbit(Bitstream bs)
		{
			if (bs.bc > 0)
				bs.bc--;
			else
			{
				bs.ptr++;
				bs.buf_index++;
				bs.bc = 7;

				if (bs.ptr == bs.end)
					// wrap call here
					bs = bs_read(bs);

				bs.sr = bs.buf[bs.buf_index];
			}

			bool result = (bs.sr & 1) > 0;
			bs.sr >>= 1;
			return result;
		}

		internal static long getbits(int nbits, Bitstream bs)
		{
			long retval;

			while (nbits > bs.bc)
			{
				bs.ptr++;
				bs.buf_index++;

				if (bs.ptr == bs.end)
					bs = bs_read(bs);

				bs.sr |= (uint)(bs.buf[bs.buf_index] << bs.bc);

				bs.bc += 8;
			}

			retval = bs.sr;

			if (bs.bc > 32)
			{
				bs.bc -= nbits;
				bs.sr = (uint)(bs.buf[bs.buf_index] >> (8 - bs.bc));
			}
			else
			{
				bs.bc -= nbits;
				bs.sr >>= nbits;
			}

			return retval;
		}

		internal static Bitstream bs_open_read(byte[] stream, int buffer_start, int buffer_end, System.IO.BinaryReader file, int file_bytes, int passed)
		{
			Bitstream bs = new Bitstream();

			bs.buf = stream;
			bs.buf_index = buffer_start;
			bs.end = buffer_end;
			bs.sr = 0;
			bs.bc = 0;

			if (passed != 0)
			{
				bs.ptr = (short)(bs.end - 1);
				bs.file_bytes = file_bytes;
				bs.file = file;
			}
			else
			{
				bs.buf_index--;
				bs.ptr = -1;
			}

			return bs;
		}

		internal static Bitstream bs_read(Bitstream bs)
		{
			if (bs.file_bytes > 0)
			{
				int bytes_read;

				var bytes_to_read = bs.buf.Length;

				if (bytes_to_read > bs.file_bytes)
					bytes_to_read = bs.file_bytes;

				try
				{
					bytes_read = bs.file.BaseStream.Read(bs.buf, 0, bytes_to_read);

					bs.buf_index = 0;
				}
				catch (System.Exception e)
				{
					System.Console.Error.WriteLine("Big error while reading file: " + e);
					bytes_read = 0;
				}

				if (bytes_read > 0)
				{
					bs.end = (short)(bytes_read);
					bs.file_bytes -= bytes_read;
				}
				else
				{
					for (int i = 0; i < bs.buf.Length; i++)
					{
						bs.buf[i] = unchecked((byte)-1);
					}
					bs.error = 1;
				}
			}
			else
			{
				bs.error = 1;

				for (int i = 0; i < bs.buf.Length; i++)
				{
					bs.buf[i] = unchecked((byte)-1);
				}
			}

			bs.ptr = 0;
			bs.buf_index = 0;

			return bs;
		}
	}
}