// -------------------------------------------------------------------------------------------------------------------------------------------------------------
// Author: 3dapi (https://github.com/3dapi)
// -------------------------------------------------------------------------------------------------------------------------------------------------------------

#define ACTIVE_GLC2DLIB

internal static class AppMain
{
#if ACTIVE_GLC2DLIB

	[STAThread]
	private static void Main(string[] args)
	{
		if(args.Contains("--self-test")){Environment.ExitCode=GameSelfTests.Run();return;}
 System.Windows.Forms.Application.SetHighDpiMode(System.Windows.Forms.HighDpiMode.PerMonitorV2);
        System.Windows.Forms.Application.EnableVisualStyles();

		using GameMain app = new();
		app.Run();
	}
#endif
}
