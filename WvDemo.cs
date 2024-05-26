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

	static WvDemo()
	{
		temp_buffer = new int[Defines.SAMPLE_BUFFER_SIZE];
	}

	public static void  Main(string[] args)
	{
		ChunkHeader FormatChunkHeader = new ChunkHeader();
		ChunkHeader DataChunkHeader = new ChunkHeader();
		RiffChunkHeader myRiffChunkHeader = new RiffChunkHeader();
		WaveHeader WaveHeader = new WaveHeader();
		byte[] myRiffChunkHeaderAsByteArray = new byte[12];
		byte[] myFormatChunkHeaderAsByteArray = new byte[8];
		byte[] myWaveHeaderAsByteArray = new byte[16];
		byte[] myDataChunkHeaderAsByteArray = new byte[8];
		
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

		System.Console.Out.WriteLine("The WavPack file has:");
		System.Console.Out.WriteLine(num_channels + " channels");
		System.Console.Out.WriteLine(bits + " bits per sample");
		System.Console.Out.WriteLine(total_samples + " samples = " + System.TimeSpan.FromTicks(total_samples * 1000 / sample_rate * 10000));

		myRiffChunkHeader.ckID[0] = 'R';
		myRiffChunkHeader.ckID[1] = 'I';
		myRiffChunkHeader.ckID[2] = 'F';
		myRiffChunkHeader.ckID[3] = 'F';
		
		myRiffChunkHeader.ckSize = total_samples * block_align + 8 * 2 + 16 + 4;
		myRiffChunkHeader.formType[0] = 'W';
		myRiffChunkHeader.formType[1] = 'A';
		myRiffChunkHeader.formType[2] = 'V';
		myRiffChunkHeader.formType[3] = 'E';
		
		FormatChunkHeader.ckID[0] = 'f';
		FormatChunkHeader.ckID[1] = 'm';
		FormatChunkHeader.ckID[2] = 't';
		FormatChunkHeader.ckID[3] = ' ';
		
		FormatChunkHeader.ckSize = 16;
		
		WaveHeader.FormatTag = 1;
		WaveHeader.NumChannels = num_channels;
		WaveHeader.SampleRate = sample_rate;
		WaveHeader.BlockAlign = block_align;
		WaveHeader.BytesPerSecond = WaveHeader.SampleRate * WaveHeader.BlockAlign;
		WaveHeader.BitsPerSample = bits;
		
		DataChunkHeader.ckID[0] = 'd';
		DataChunkHeader.ckID[1] = 'a';
		DataChunkHeader.ckID[2] = 't';
		DataChunkHeader.ckID[3] = 'a';
		DataChunkHeader.ckSize = total_samples * block_align;
		
		myRiffChunkHeaderAsByteArray[0] = (byte) myRiffChunkHeader.ckID[0];
		myRiffChunkHeaderAsByteArray[1] = (byte) myRiffChunkHeader.ckID[1];
		myRiffChunkHeaderAsByteArray[2] = (byte) myRiffChunkHeader.ckID[2];
		myRiffChunkHeaderAsByteArray[3] = (byte) myRiffChunkHeader.ckID[3];
		
		// swap endians here
		
		myRiffChunkHeaderAsByteArray[7] = (byte) (SupportClass.URShift(myRiffChunkHeader.ckSize, 24));
		myRiffChunkHeaderAsByteArray[6] = (byte) (SupportClass.URShift(myRiffChunkHeader.ckSize, 16));
		myRiffChunkHeaderAsByteArray[5] = (byte) (SupportClass.URShift(myRiffChunkHeader.ckSize, 8));
		myRiffChunkHeaderAsByteArray[4] = (byte) (myRiffChunkHeader.ckSize);
		
		myRiffChunkHeaderAsByteArray[8] = (byte) myRiffChunkHeader.formType[0];
		myRiffChunkHeaderAsByteArray[9] = (byte) myRiffChunkHeader.formType[1];
		myRiffChunkHeaderAsByteArray[10] = (byte) myRiffChunkHeader.formType[2];
		myRiffChunkHeaderAsByteArray[11] = (byte) myRiffChunkHeader.formType[3];
		
		myFormatChunkHeaderAsByteArray[0] = (byte) FormatChunkHeader.ckID[0];
		myFormatChunkHeaderAsByteArray[1] = (byte) FormatChunkHeader.ckID[1];
		myFormatChunkHeaderAsByteArray[2] = (byte) FormatChunkHeader.ckID[2];
		myFormatChunkHeaderAsByteArray[3] = (byte) FormatChunkHeader.ckID[3];
		
		// swap endians here
		myFormatChunkHeaderAsByteArray[7] = (byte) (SupportClass.URShift(FormatChunkHeader.ckSize, 24));
		myFormatChunkHeaderAsByteArray[6] = (byte) (SupportClass.URShift(FormatChunkHeader.ckSize, 16));
		myFormatChunkHeaderAsByteArray[5] = (byte) (SupportClass.URShift(FormatChunkHeader.ckSize, 8));
		myFormatChunkHeaderAsByteArray[4] = (byte) (FormatChunkHeader.ckSize);
		
		// swap endians
		myWaveHeaderAsByteArray[1] = (byte) (SupportClass.URShift(WaveHeader.FormatTag, 8));
		myWaveHeaderAsByteArray[0] = (byte) (WaveHeader.FormatTag);
		
		// swap endians
		myWaveHeaderAsByteArray[3] = (byte) (SupportClass.URShift(WaveHeader.NumChannels, 8));
		myWaveHeaderAsByteArray[2] = (byte) WaveHeader.NumChannels;
		
		
		// swap endians
		myWaveHeaderAsByteArray[7] = (byte) (SupportClass.URShift(WaveHeader.SampleRate, 24));
		myWaveHeaderAsByteArray[6] = (byte) (SupportClass.URShift(WaveHeader.SampleRate, 16));
		myWaveHeaderAsByteArray[5] = (byte) (SupportClass.URShift(WaveHeader.SampleRate, 8));
		myWaveHeaderAsByteArray[4] = (byte) (WaveHeader.SampleRate);
		
		// swap endians
		
		myWaveHeaderAsByteArray[11] = (byte) (SupportClass.URShift(WaveHeader.BytesPerSecond, 24));
		myWaveHeaderAsByteArray[10] = (byte) (SupportClass.URShift(WaveHeader.BytesPerSecond, 16));
		myWaveHeaderAsByteArray[9] = (byte) (SupportClass.URShift(WaveHeader.BytesPerSecond, 8));
		myWaveHeaderAsByteArray[8] = (byte) (WaveHeader.BytesPerSecond);
		
		// swap endians
		myWaveHeaderAsByteArray[13] = (byte) (SupportClass.URShift(WaveHeader.BlockAlign, 8));
		myWaveHeaderAsByteArray[12] = (byte) WaveHeader.BlockAlign;
		
		// swap endians
		myWaveHeaderAsByteArray[15] = (byte) (SupportClass.URShift(WaveHeader.BitsPerSample, 8));
		myWaveHeaderAsByteArray[14] = (byte) WaveHeader.BitsPerSample;
		
		myDataChunkHeaderAsByteArray[0] = (byte) DataChunkHeader.ckID[0];
		myDataChunkHeaderAsByteArray[1] = (byte) DataChunkHeader.ckID[1];
		myDataChunkHeaderAsByteArray[2] = (byte) DataChunkHeader.ckID[2];
		myDataChunkHeaderAsByteArray[3] = (byte) DataChunkHeader.ckID[3];
		
		// swap endians
		
		myDataChunkHeaderAsByteArray[7] = (byte) (SupportClass.URShift(DataChunkHeader.ckSize, 24));
		myDataChunkHeaderAsByteArray[6] = (byte) (SupportClass.URShift(DataChunkHeader.ckSize, 16));
		myDataChunkHeaderAsByteArray[5] = (byte) (SupportClass.URShift(DataChunkHeader.ckSize, 8));
		myDataChunkHeaderAsByteArray[4] = (byte) DataChunkHeader.ckSize;
		
		try
		{
			using (var fostream = new System.IO.FileStream(System.IO.Path.ChangeExtension(inputWVFile, ".wav"), System.IO.FileMode.Create))
			{
				SupportClass.WriteOutput(fostream, myRiffChunkHeaderAsByteArray);
				SupportClass.WriteOutput(fostream, myFormatChunkHeaderAsByteArray);
				SupportClass.WriteOutput(fostream, myWaveHeaderAsByteArray);
				SupportClass.WriteOutput(fostream, myDataChunkHeaderAsByteArray);

				var sw = new System.Diagnostics.Stopwatch();
				sw.Start();

				var samples_unpack = Defines.SAMPLE_BUFFER_SIZE / num_channels;

				var loop_samples = total_samples / 100 / samples_unpack * samples_unpack;

				while (true)
				{
					long samples_unpacked;

					samples_unpacked = WavPackUtils.WavpackUnpackSamples(wpc, temp_buffer, samples_unpack);

					total_unpacked_samples += samples_unpacked;

					if (samples_unpacked > 0)
					{
						samples_unpacked = samples_unpacked * num_channels;

						format_samples(temp_buffer, samples_unpacked, block_align, byteps);
						fostream.Write(pcm_buffer, 0, (int)samples_unpacked * byteps);
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
					pcm_buffer[counter++] = (byte)(SupportClass.URShift(temp, 8));
					pcm_buffer[counter++] = (byte)(SupportClass.URShift(temp, 16));
					pcm_buffer[counter++] = (byte)(SupportClass.URShift(temp, 24));
					counter2++;
					samcnt--;
				}

				break;
		}
	}
}