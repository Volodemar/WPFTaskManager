using IWshRuntimeLibrary;
using Microsoft.Win32;
using System;
using System.Drawing;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Navigation;
using System.Windows.Shapes;
using System.Windows.Interop;
using System.Threading;
using System.ComponentModel;

namespace TestAuslogics
{
	public partial class MainWindow : Window
	{
		public List<Programs> LProgram { get; set; }

		public MainWindow()
		{
			InitializeComponent();

			LProgram = new List<Programs>();

			BackgroundWorker worker = new BackgroundWorker();
			worker.WorkerReportsProgress = true;
			worker.DoWork += worker_DoWork;
			worker.ProgressChanged += worker_ProgressChanged;
			worker.RunWorkerCompleted += worker_RunWorkerCompleted;
			worker.RunWorkerAsync();

			///Получение данных в основном потоке 
			///(Закоментарить чтобы вернуться к проблеме, что таблица не обновляется после возврата данных из паралельного потока)

			LProgram.AddRange(GetProgramRegistry(Registry.CurrentUser, @"Software\Microsoft\Windows\CurrentVersion\Run"));
			LProgram.AddRange(GetProgramRegistry(Registry.LocalMachine, @"Software\Microsoft\Windows\CurrentVersion\Run"));
			LProgram.AddRange(GetProgramsStartMenu(Environment.SpecialFolder.CommonStartup));
			LProgram.AddRange(GetProgramsStartMenu(Environment.SpecialFolder.Startup));
			autoFile.DataContext = this;
		}

		/// <summary>
		/// Выполнение загрузки данных в потоке
		/// </summary>
        private void worker_DoWork(object sender, DoWorkEventArgs e) 
        {
			List<Programs> workerLoad = new List<Programs>();

			workerLoad.AddRange(GetProgramRegistry(Registry.CurrentUser, @"Software\Microsoft\Windows\CurrentVersion\Run"));
			(sender as BackgroundWorker).ReportProgress(30);
			Thread.Sleep(1000);

			workerLoad.AddRange(GetProgramRegistry(Registry.LocalMachine, @"Software\Microsoft\Windows\CurrentVersion\Run"));
			(sender as BackgroundWorker).ReportProgress(60);
			Thread.Sleep(1000);

			workerLoad.AddRange(GetProgramsStartMenu(Environment.SpecialFolder.CommonStartup));
			(sender as BackgroundWorker).ReportProgress(80);
			Thread.Sleep(1000);

			workerLoad.AddRange(GetProgramsStartMenu(Environment.SpecialFolder.Startup));
			(sender as BackgroundWorker).ReportProgress(100);

			e.Result = workerLoad;
        }

		/// <summary>
		/// Асинхронный поток завершен
		/// </summary>
		private void worker_RunWorkerCompleted(object sender, RunWorkerCompletedEventArgs e)
		{
			//Данные начитываются корректно
			LProgram.Clear();
			LProgram.AddRange((List<Programs>)e.Result);
			pbStatus.Value = 0;

			//Визуального обновления данных не происходит!!! Причину выяснить пока не смог, пробовал много всяких вариантов.
			//autoFile.Dispatcher.BeginInvoke(new Action(() => autoFile.ItemsSource = LProgram));

			//BindingExpression binding = autoFile.GetBindingExpression(ListView.ItemsSourceProperty);
			//binding.UpdateSource();
		}

		/// <summary>
		/// Отобразить прогресс
		/// </summary>
		void worker_ProgressChanged(object sender, ProgressChangedEventArgs e)
		{
			pbStatus.Value = e.ProgressPercentage;
		}

		/// <summary>
		/// Заполняет лист программами по указанному пути реестра
		/// </summary>
		private List<Programs> GetProgramRegistry(RegistryKey rk, string RegistryPath)
		{
			List<Programs> LPLoad = new List<Programs>();

			using(RegistryKey regKey = rk.OpenSubKey(RegistryPath, false))
			{
				foreach (string keyName in regKey.GetValueNames())
				{
					//Получаем корректный путь
					string fullPath  = (string)regKey.GetValue(keyName);
					string prms		 = "";
					if(fullPath.IndexOf("\"") > -1)
					{
						prms = fullPath.Substring(fullPath.LastIndexOf("\"")+1);
						fullPath = fullPath.Remove(fullPath.LastIndexOf("\""));
						fullPath = fullPath.Substring(fullPath.IndexOf("\"")+1);
					}

					if(System.IO.File.Exists(fullPath))
					{
						//Получаем иконку
						System.Drawing.Icon ico = System.Drawing.Icon.ExtractAssociatedIcon(fullPath);

						Programs prog = new Programs();
						prog.Icon		= ToImageSource(ico);
						prog.FileName	= System.IO.Path.GetFileName(fullPath);
						prog.Params		= prms;
						prog.Path		= System.IO.Path.GetDirectoryName(fullPath);
						prog.Type		= "Registry";

						LPLoad.Add(prog);
					}
				}
			}

			return LPLoad;
		}

		/// <summary>
		/// Заполняет лист программами из указанной системной директории
		/// </summary>
		private List<Programs> GetProgramsStartMenu(Environment.SpecialFolder sf)
		{
			List<Programs> LPLoad = new List<Programs>();

			//Получаем путь к папке автозагрузки пользователя
			string StartFolder = Environment.GetFolderPath(sf);
			string [] UfileEntries = System.IO.Directory.GetFiles(StartFolder);
			foreach(string fileName in UfileEntries)
			{
					//Если ярлык
					string prms = "";
					string path = System.IO.Path.GetDirectoryName(fileName);

						if(System.IO.Path.GetExtension(fileName) == ".lnk")
						{
							prms = GetShortcutParameters(fileName);
							path = GetShortcutTargetFile(fileName);
						}

					if(System.IO.File.Exists(fileName))
					{
						//Получаем иконку
						System.Drawing.Icon ico = System.Drawing.Icon.ExtractAssociatedIcon(fileName);

						Programs prog = new Programs();
						prog.Icon		= ToImageSource(ico);
						prog.FileName	= System.IO.Path.GetFileName(fileName);
						prog.Params		= prms;
						prog.Path		= path;
						prog.Type		= "Start Menu";

						LPLoad.Add(prog);
					}
			}

			return LPLoad;
		}

		/// <summary>
		/// Конвертирует тип картинки Icon в ImageSource
		/// </summary>
		public static ImageSource ToImageSource(Icon icon)
		{            
			Bitmap bitmap = icon.ToBitmap();
			IntPtr hBitmap = bitmap.GetHbitmap();

			ImageSource wpfBitmap = Imaging.CreateBitmapSourceFromHBitmap(hBitmap, IntPtr.Zero,	Int32Rect.Empty, BitmapSizeOptions.FromEmptyOptions());

			return wpfBitmap;
		}

		/// <summary>
		/// Возвращаем цель ярлыка
		/// </summary>
        private string GetShortcutTargetFile(string linkPathName)
        {
            string shortcutTarget = "";
            if (System.IO.File.Exists(linkPathName))
            {                
                WshShell shell = new WshShell();
                IWshShortcut link = (IWshShortcut)shell.CreateShortcut(linkPathName);
                shortcutTarget = link.TargetPath;
            }
            return shortcutTarget;
        }

		/// <summary>
		/// Возвращаем парамеры ярлыка
		/// </summary>
        private string GetShortcutParameters(string linkPathName)
        {
            string shortcutTarget = "";
            if (System.IO.File.Exists(linkPathName))
            {                
                WshShell shell = new WshShell();
                IWshShortcut link = (IWshShortcut)shell.CreateShortcut(linkPathName);
                shortcutTarget = link.Arguments;
            }
            return shortcutTarget;
        }
	}
}
