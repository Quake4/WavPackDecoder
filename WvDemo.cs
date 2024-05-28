/*
** WvDemo.cs
**
** Copyright (c) 2010-2016 Peter McQuillan
**
** All Rights Reserved.
**                       
** Distributed under the BSD Software License (see license.txt)  
***/

public class WvDemo
{
	internal static int[] temp_buffer;
	internal static byte[] pcm_buffer;

	public static int Main(string[] args)
	{
		long total_unpacked_samples = 0;
		WavpackContext wpc;
		System.IO.FileStream fistream;
		System.IO.BinaryReader reader;
		
		string inputWVFile = args.Length > 0 ? args[0] : "input.wv";
		
		try
		{
			fistream = System.IO.File.OpenRead(inputWVFile);
			reader = new System.IO.BinaryReader(fistream);
			wpc = WavPackUtils.WavpackOpenFileInput(reader);
		}
		catch (System.IO.FileNotFoundException)
		{
			System.Console.Error.WriteLine("Input file '" + inputWVFile +"' not found");
			return 1;
		}
		catch (System.IO.DirectoryNotFoundException)
		{
			System.Console.Error.WriteLine("Input file '" + inputWVFile + "' not found - invalid directory");
			return 1;
		}

		if (!string.IsNullOrEmpty(wpc.error_message))
		{
			System.Console.Error.WriteLine("Error: " + wpc.error_message);
			return 1;
		}

		int num_channels = WavPackUtils.WavpackGetReducedChannels(wpc);
		int bits = WavPackUtils.WavpackGetBitsPerSample(wpc);
		int byteps = WavPackUtils.WavpackGetBytesPerSample(wpc);
		int block_align = byteps * num_channels;
		long total_samples = WavPackUtils.WavpackGetNumSamples(wpc);
		long sample_rate = WavPackUtils.WavpackGetSampleRate(wpc);
		var lossy = WavPackUtils.WavpackLossyBlocks(wpc);

		var version = wpc.stream.wphdr.version;

		System.Console.Out.WriteLine("The WavPack " + (wpc.five ? "5" : "4") + " (" + (version >> 8) + "." + (version & 0xFF) + ")" +
			" file '" + System.IO.Path.GetFileName(inputWVFile) + "' has:");
		System.Console.Out.WriteLine(wpc.file_format + " format");
		System.Console.Out.WriteLine(num_channels + " channels");
		System.Console.Out.WriteLine(bits + " bits per sample");
		System.Console.Out.WriteLine(total_samples + " samples = " + System.TimeSpan.FromTicks(total_samples * 1000 / sample_rate * 10000));
		System.Console.Out.WriteLine((lossy ? "Lossy" : "Losseless") + " decoding");

		try
		{
			using (var fostream = new System.IO.FileStream(System.IO.Path.ChangeExtension(inputWVFile, WavPackUtils.WavpackGetFileExtension(wpc)), System.IO.FileMode.Create))
			{
				if (wpc.header != null && (wpc.config.flags & Defines.CONFIG_FLOAT_DATA) == 0)
					fostream.Write(wpc.header, 0, wpc.header.Length);
				else
				{
					var riffChunkHeader = new RiffChunkHeader((uint)(total_samples * block_align + 2 * ChunkHeader.Size + WaveHeader.Size));

					var formatChunkHeader = new ChunkHeader("fmt ", WaveHeader.Size);

					var waveHeader = new WaveHeader()
					{
						FormatTag = 1,
						NumChannels = (ushort)num_channels,
						SampleRate = (uint)sample_rate,
						BitsPerSample = (ushort)bits,
						BlockAlign = (ushort)block_align,
						BytesPerSecond = (uint)(sample_rate * block_align),
					};

					var dataChunkHeader = new ChunkHeader("data", (uint)(total_samples * block_align));

					void WriteOutput(byte[] bytes)
					{
						fostream.Write(bytes, 0, bytes.Length);
					}

					WriteOutput(riffChunkHeader.AsBytes());
					WriteOutput(formatChunkHeader.AsBytes());
					WriteOutput(waveHeader.AsBytes());
					WriteOutput(dataChunkHeader.AsBytes());
				}

				var sw = new System.Diagnostics.Stopwatch();
				sw.Start();

				var samples_unpack = Defines.SAMPLE_BUFFER_SIZE;

				var loop_samples = total_samples / 100 / samples_unpack * samples_unpack;

				temp_buffer = new int[samples_unpack * num_channels];

				while (true)
				{
					long samples_unpacked = WavPackUtils.WavpackUnpackSamples(wpc, temp_buffer, samples_unpack);

					total_unpacked_samples += samples_unpacked;

					if (samples_unpacked > 0)
					{
						format_samples(temp_buffer, samples_unpacked * num_channels, block_align, byteps);
						fostream.Write(pcm_buffer, 0, (int)samples_unpacked * block_align);
					}

					if (total_unpacked_samples % loop_samples == 0)
						System.Console.Out.Write("Process: " + total_unpacked_samples * 100 / total_samples + "%\r");

					if (samples_unpacked == 0)
						break;
				} // end of while

				System.Console.Out.WriteLine(sw.ElapsedMilliseconds + " milliseconds to process WavPack file in main loop");

				if (wpc.trailer != null)
					fostream.Write(wpc.trailer, 0, wpc.trailer.Length);
			}
		}
		catch (System.Exception e)
		{
			System.Console.Error.WriteLine("Error when writing wav file, sorry: ");
			System.Console.Error.Write(e.StackTrace);
			return 1;
		}

		fistream?.Dispose();

		if ((WavPackUtils.WavpackGetNumSamples(wpc) != - 1) && (total_unpacked_samples != WavPackUtils.WavpackGetNumSamples(wpc)))
		{
			System.Console.Error.WriteLine("Incorrect number of samples");
			return 1;
		}

		var crc_count = WavPackUtils.WavpackGetNumErrors(wpc);
		if (crc_count > 0)
		{
			System.Console.Error.WriteLine(crc_count + " CRC errors detected");
			return 1;
		}

		return 0;
	}


	// Reformat samples from longs in processor's native endian mode to
	// little-endian data with (possibly) less than 4 bytes / sample.

	internal static void format_samples(int[] src, long samcnt, int block_align, int bps)
	{
		int temp;
		int counter = 0;
		int counter2 = 0;

		var len = samcnt * block_align;
		if (pcm_buffer == null || pcm_buffer.Length < len)
			pcm_buffer = new byte[len];

		switch (bps)
		{
			case 1:
				while (samcnt-- > 0)
					pcm_buffer[counter++] = (byte)(0x00FF & (src[counter2++] + 128));
				break;

			case 2:
				while (samcnt-- > 0)
				{
					temp = src[counter2++];
					pcm_buffer[counter++] = (byte)temp;
					pcm_buffer[counter++] = (byte)(temp >> 8);
				}

				break;

			case 3:
				while (samcnt-- > 0)
				{
					temp = src[counter2++];
					pcm_buffer[counter++] = (byte)temp;
					pcm_buffer[counter++] = (byte)(temp >> 8);
					pcm_buffer[counter++] = (byte)(temp >> 16);
				}

				break;

			case 4:
				while (samcnt-- > 0)
				{
					temp = src[counter2++];
					pcm_buffer[counter++] = (byte)temp;
					pcm_buffer[counter++] = (byte)(temp >> 8);
					pcm_buffer[counter++] = (byte)(temp >> 16);
					pcm_buffer[counter++] = (byte)SupportClass.URShift(temp, 24); // with sign
				}

				break;
		}
	}
}