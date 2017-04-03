using System.Threading.Tasks;
using Windows.Storage;
using Windows.Media.SpeechRecognition;
using Windows.UI.Core;
using Windows.ApplicationModel;
using Windows.ApplicationModel.Resources.Core;
using Windows.Globalization;

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices.WindowsRuntime;
using Windows.Foundation;
using Windows.Foundation.Collections;
using Windows.UI.Xaml;
using Windows.UI.Xaml.Controls;
using Windows.UI.Xaml.Controls.Primitives;
using Windows.UI.Xaml.Data;
using Windows.UI.Xaml.Input;
using Windows.UI.Xaml.Media;
using Windows.UI.Xaml.Navigation;

// La plantilla de elemento Página en blanco está documentada en https://go.microsoft.com/fwlink/?LinkId=402352&clcid=0xc0a

namespace voice03
{
    /// <summary>
    /// Página vacía que se puede usar de forma independiente o a la que se puede navegar dentro de un objeto Frame.
    /// </summary>
    public sealed partial class MainPage : Page
    {
        //mis objetillos
        private SpeechRecognizer speechRecognizer;
        private IAsyncOperation<SpeechRecognitionResult> recognitionOperation;
        private CoreDispatcher dispatcher;
        private ResourceContext speechContext;
        private ResourceMap sppechResourceMap;
        

        public MainPage()
        {
            this.InitializeComponent();
        }

        protected async override void OnNavigatedTo(NavigationEventArgs e) //cuando llegas
        {
            dispatcher = CoreWindow.GetForCurrentThread().Dispatcher;

            //comprobación de si tengo permiso sobre el micrófono; si tengo, inicio el proceso (InitializeRecognizer)
            bool tengoPermiso = await AudioCapturePermissions.RequestMicrophonePermission();
            if (tengoPermiso)
            {
                Language speechLanguage = SpeechRecognizer.SystemSpeechLanguage;                
                await InitializeRecognizer(speechLanguage);

                //a reconocer
                try
                {
                    recognitionOperation = speechRecognizer.RecognizeWithUIAsync();
                    SpeechRecognitionResult speechRecognitionResult = await recognitionOperation;
                    if (speechRecognitionResult.Status == SpeechRecognitionResultStatus.Success)
                    {
                        HandleRecognitionResult(speechRecognitionResult);
                    }
                    else
                    {
                        resultadosTB.Visibility = Visibility.Visible;
                        resultadosTB.Text = string.Format("Error de reconocimiento; estado: {0}", speechRecognitionResult.Status.ToString());
                    }
                }

            catch (TaskCanceledException ex)
                {
                    System.Diagnostics.Debug.WriteLine("Me cerraron en el medio del reconocimiento");
                    System.Diagnostics.Debug.WriteLine(ex.ToString());
                }
            catch (Exception ex2)
                {
                    var msg = new Windows.UI.Popups.MessageDialog(ex2.Message, "Error genérico");
                    await msg.ShowAsync();
                }
            } else
            {
                resultadosTB.Visibility = Visibility.Visible;
                resultadosTB.Text = "Sin acceso al micrófono";
            }
        }

        private async Task InitializeRecognizer(Language recognizerLanguage)
        {   //inicialiación del reconocedor
            if (speechRecognizer != null)
            {
                speechRecognizer.StateChanged -= SpeechRecognizer_StateChanged;

                this.speechRecognizer.Dispose();
                this.speechRecognizer = null;
            }

            try
            {
                //cargar el fichero de la grmática
                string fileName = String.Format("SRGS\\SRGSColors.xml");
                StorageFile grammaContentFile = await Package.Current.InstalledLocation.GetFileAsync(fileName);

                //inicializamos el objeto reconocedor           
                speechRecognizer = new SpeechRecognizer(recognizerLanguage);

                //activa el feedback al usuario
                speechRecognizer.StateChanged += SpeechRecognizer_StateChanged;

                //compilamos la gramática
                SpeechRecognitionGrammarFileConstraint grammarConstraint = new SpeechRecognitionGrammarFileConstraint(grammaContentFile);
                speechRecognizer.Constraints.Add(grammarConstraint);
                SpeechRecognitionCompilationResult compilationResult = await speechRecognizer.CompileConstraintsAsync();

                //si no hubo éxito...
                if (compilationResult.Status != SpeechRecognitionResultStatus.Success)
                {
                    resultadosTB.Visibility = Visibility.Visible;
                    resultadosTB.Text = "Error compilando la gramática";
                }
                else
                {
                    //si hubo éxito 
                    resultadosTB.Visibility = Visibility.Visible;
                    resultadosTB.Text = "Gramática compilada, a por ello";

                    speechRecognizer.Timeouts.EndSilenceTimeout = TimeSpan.FromSeconds(1.2);//damos tiempo a hablar

                }
            }
            catch (Exception e)
            {
                var messageDialog = new Windows.UI.Popups.MessageDialog(e.Message, "Excepción chunga: ");
                await messageDialog.ShowAsync();
            }            

        }

        private async void SpeechRecognizer_StateChanged(SpeechRecognizer sender, SpeechRecognizerStateChangedEventArgs args)
        {//feedback al usuario del estado del reconocimiento de texto
            await dispatcher.RunAsync(CoreDispatcherPriority.Normal, () =>
            {
            resultadosTB.Text = "Estado de reconocimiento de texto: " + args.State.ToString();
            });
        }

        private void HandleRecognitionResult (SpeechRecognitionResult ecoResult)
        {

        }

        private void Button_Click(object sender, RoutedEventArgs e)
        {

        }
    }
}
