//
// In order to convert some functionality to Visual C#, the Java Language Conversion Assistant
// creates "support classes" that duplicate the original functionality.  
//
// Support classes replicate the functionality of the original code, but in some cases they are 
// substantially different architecturally. Although every effort is made to preserve the 
// original architecture of the application in the converted project, the user should be aware that 
// the primary goal of these support classes is to replicate functionality, and that at times 
// the architecture of the resulting solution may differ somewhat.
//

namespace WavPack
{
	/// <summary>
	/// Contains conversion support elements such as classes, interfaces and static methods.
	/// </summary>
	public class SupportClass
	{
		/*******************************/
		/// <summary>
		/// Performs an unsigned bitwise right shift with the specified number
		/// </summary>
		/// <param name="number">Number to operate on</param>
		/// <param name="bits">Ammount of bits to shift</param>
		/// <returns>The resulting number from the shift operation</returns>
		public static int URShift(int number, int bits)
		{
			if (number >= 0)
				return number >> bits;
			else
				return (number >> bits) + (2 << ~bits);
		}
	}
}