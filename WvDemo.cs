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

	public static void  Main(string[] args)
	{
		long total_unpacked_samples = 0;
		WavpackContext wpc = new WavpackContext();
		System.IO.FileStream fistream = null;
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
			System.Environment.Exit(1);
		}
		catch (System.IO.DirectoryNotFoundException)
		{
			System.Console.Error.WriteLine("Input file '" + inputWVFile + "' not found - invalid directory");
			System.Environment.Exit(1);
		}
		
		if (!string.IsNullOrEmpty(wpc.error_message))
		{
			System.Console.Error.WriteLine("Error: " + wpc.error_message);
			System.Environment.Exit(1);
		}

		int num_channels = WavPackUtils.WavpackGetReducedChannels(wpc);
		int bits = WavPackUtils.WavpackGetBitsPerSample(wpc);
		int byteps = WavPackUtils.WavpackGetBytesPerSample(wpc);
		int block_align = byteps * num_channels;
		long total_samples = WavPackUtils.WavpackGetNumSamples(wpc);
		long sample_rate = WavPackUtils.WavpackGetSampleRate(wpc);

		System.Console.Out.WriteLine("The WavPack " + (wpc.five ? "5" : "4") + " file '" + System.IO.Path.GetFileName(inputWVFile) + "' has:");
		System.Console.Out.WriteLine(num_channels + " channels");
		System.Console.Out.WriteLine(bits + " bits per sample");
		System.Console.Out.WriteLine(total_samples + " samples = " + System.TimeSpan.FromTicks(total_samples * 1000 / sample_rate * 10000));

		if (num_channels > 2)
		{
			System.Console.Error.WriteLine("Only two channels supported");
			System.Environment.Exit(1);
		}

		try
		{
			using (var fostream = new System.IO.FileStream(System.IO.Path.ChangeExtension(inputWVFile, WavPackUtils.WavpackGetFileExtension(wpc)), System.IO.FileMode.Create))
			{
				if (wpc.riff_header != null && (wpc.config.flags & Defines.CONFIG_FLOAT_DATA) == 0)
					fostream.Write(wpc.riff_header, 0, wpc.riff_header.Length);
				else
				{
					var riffChunkHeader = new RiffChunkHeader((uint)(total_samples * block_align + 8 * 2 + 16 + 4));

					var formatChunkHeader = new ChunkHeader("fmt ", 16);

					var waveHeader = new WaveHeader();
					waveHeader.FormatTag = 1;
					waveHeader.NumChannels = (ushort)num_channels;
					waveHeader.SampleRate = (uint)sample_rate;
					waveHeader.BlockAlign = (ushort)block_align;
					waveHeader.BytesPerSecond = waveHeader.SampleRate * waveHeader.BlockAlign;
					waveHeader.BitsPerSample = (ushort)bits;

					var dataChunkHeader = new ChunkHeader("data", (uint)(total_samples * block_align));

					SupportClass.WriteOutput(fostream, riffChunkHeader.AsBytes());
					SupportClass.WriteOutput(fostream, formatChunkHeader.AsBytes());
					SupportClass.WriteOutput(fostream, waveHeader.AsBytes());
					SupportClass.WriteOutput(fostream, dataChunkHeader.AsBytes());
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
			}
		}
		catch (System.Exception e)
		{
			System.Console.Error.WriteLine("Error when writing wav file, sorry: ");
			SupportClass.WriteStackTrace(e, System.Console.Error);
			System.Environment.Exit(1);
		}

		fistream?.Dispose();

		if ((WavPackUtils.WavpackGetNumSamples(wpc) != - 1) && (total_unpacked_samples != WavPackUtils.WavpackGetNumSamples(wpc)))
		{
			System.Console.Error.WriteLine("Incorrect number of samples");
			System.Environment.Exit(1);
		}
		
		if (WavPackUtils.WavpackGetNumErrors(wpc) > 0)
		{
			System.Console.Error.WriteLine("CRC errors detected");
			System.Environment.Exit(1);
		}
		
		System.Environment.Exit(0);
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
				while (samcnt > 0)
				{
					pcm_buffer[counter++] = (byte)(0x00FF & (src[counter] + 128));
					samcnt--;
				}
				break;

			case 2:
				while (samcnt > 0)
				{
					temp = src[counter2];
					pcm_buffer[counter++] = (byte)temp;
					pcm_buffer[counter++] = (byte)(temp >> 8);
					counter2++;
					samcnt--;
				}

				break;

			case 3:
				while (samcnt > 0)
				{
					temp = src[counter2];
					pcm_buffer[counter++] = (byte)temp;
					pcm_buffer[counter++] = (byte)(temp >> 8);
					pcm_buffer[counter++] = (byte)(temp >> 16);
					counter2++;
					samcnt--;
				}

				break;

			case 4:
				while (samcnt > 0)
				{
					temp = src[counter2];
					pcm_buffer[counter++] = (byte)temp;
					pcm_buffer[counter++] = (byte)SupportClass.URShift(temp, 8);
					pcm_buffer[counter++] = (byte)SupportClass.URShift(temp, 16);
					pcm_buffer[counter++] = (byte)SupportClass.URShift(temp, 24);
					counter2++;
					samcnt--;
				}

				break;
		}
	}
}