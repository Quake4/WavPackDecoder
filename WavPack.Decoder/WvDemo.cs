/*
** WvDemo.cs
**
** Copyright (c) 2010-2016 Peter McQuillan
**
** All Rights Reserved.
**                       
** Distributed under the BSD Software License (see license.txt)  
***/

namespace WavPack.Decoder
{
public class WvDemo
{
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
			System.Console.Error.WriteLine("Input file '" + inputWVFile + "' not found");
			return 1;
		}
		catch (System.IO.DirectoryNotFoundException)
		{
			System.Console.Error.WriteLine("Input file '" + inputWVFile + "' not found - invalid directory");
			return 1;
		}

		var error = WavPackUtils.WavpackGetErrorMessage(wpc);
		if (!string.IsNullOrEmpty(error))
		{
			System.Console.Error.WriteLine("Error: " + error);
			return 1;
		}

		int num_channels = WavPackUtils.WavpackGetReducedChannels(wpc);
		int bits = WavPackUtils.WavpackGetBitsPerSample(wpc);
		int byteps = WavPackUtils.WavpackGetBytesPerSample(wpc);
		int block_align = byteps * num_channels;
		long total_samples = WavPackUtils.WavpackGetNumSamples(wpc);
		long sample_rate = WavPackUtils.WavpackGetSampleRate(wpc);
		var lossy = WavPackUtils.WavpackLossy(wpc);
		var version = WavPackUtils.WavpackGetVersion(wpc);
		var compressionLevel = WavPackUtils.WavpackGetCompressionLevel(wpc);

		System.Console.Out.WriteLine("The WavPack " + (WavPackUtils.WavpackGetIsFive(wpc) ? "5" : "4") +
			" (" + (version >> 8) + "." + (version & 0xFF) + ")" +
			" file '" + System.IO.Path.GetFileName(inputWVFile) + "' has:");
		System.Console.Out.WriteLine(WavPackUtils.WavpackGetFileFormat(wpc) + " format");
		System.Console.Out.WriteLine(num_channels + " channels");
		System.Console.Out.WriteLine(bits + " bits per sample");
		System.Console.Out.WriteLine(sample_rate + " samples/s");
		System.Console.Out.WriteLine(total_samples + " total samples = " + System.TimeSpan.FromTicks(total_samples * 1000 / sample_rate * 10000));
		System.Console.Out.WriteLine((lossy ? "Lossy" : "Losseless") + " decoding");
		if (compressionLevel != null)
			System.Console.Out.WriteLine(compressionLevel + " compression level");

		try
		{
			using (var fostream = new System.IO.FileStream(System.IO.Path.ChangeExtension(inputWVFile, WavPackUtils.WavpackGetFileExtension(wpc)), System.IO.FileMode.Create))
			{
				var header = WavPackUtils.WavpackGetHeader(wpc);

				if (header != null && !WavPackUtils.WavpackGetIsFloat(wpc))
					fostream.Write(header, 0, header.Length);
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

				int[] temp_buffer = new int[samples_unpack * num_channels];
				byte[] pcm_buffer = new byte[samples_unpack * block_align];

				while (true)
				{
					long samples_unpacked = WavPackUtils.WavpackUnpackSamples(wpc, temp_buffer, samples_unpack);

					total_unpacked_samples += samples_unpacked;

					if (samples_unpacked > 0)
					{
						if (!WavPackUtils.WavpackFormatSamples(temp_buffer, samples_unpacked * num_channels, byteps, pcm_buffer))
							break;
						fostream.Write(pcm_buffer, 0, (int)samples_unpacked * block_align);
					}

					if (total_unpacked_samples % loop_samples == 0)
						System.Console.Out.Write("Process: " + total_unpacked_samples * 100 / total_samples + "%\r");

					if (samples_unpacked == 0)
						break;
				} // end of while

				System.Console.Out.WriteLine(sw.ElapsedMilliseconds + " milliseconds to process WavPack file in main loop");

				var trailer = WavPackUtils.WavpackGetTrailer(wpc);
				if (trailer != null)
					fostream.Write(trailer, 0, trailer.Length);
			}
		}
		catch (System.Exception e)
		{
			System.Console.Error.WriteLine("Error when writing wav file, sorry: ");
			System.Console.Error.Write(e.StackTrace);
			return 1;
		}

		fistream?.Dispose();

		var num_samples = WavPackUtils.WavpackGetNumSamples(wpc);
		if (num_samples != -1 && total_unpacked_samples != num_samples)
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
}
}