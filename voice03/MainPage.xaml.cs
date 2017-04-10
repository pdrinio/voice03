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

                reconocerContinuamente();
               
            } else
            {
                tbEstadoReconocimiento.Visibility = Visibility.Visible;
                tbEstadoReconocimiento.Text = "Sin acceso al micrófono";
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
                string fileName = String.Format("SRGS\\SRGSComandos.xml");
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
                    tbEstadoReconocimiento.Visibility = Visibility.Visible;
                    tbEstadoReconocimiento.Text = "Error compilando la gramática";
                }
                else
                {
                    //si hubo éxito 
                    tbEstadoReconocimiento.Visibility = Visibility.Visible;
                    tbEstadoReconocimiento.Text = "Gramática compilada, reconociendo";

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
                tbEstadoReconocimiento.Text = "Estado de reconocimiento de texto: " + args.State.ToString();
            });
        }

        private async void reconocerContinuamente()
        {
            try
            {
                //recognitionOperation = speechRecognizer.RecognizeWithUIAsync(); pasamos el feedback que da....
                recognitionOperation = speechRecognizer.RecognizeAsync(); //y utilizamos éste, que no muestra el pop-up
                SpeechRecognitionResult speechRecognitionResult = await recognitionOperation;
                if (speechRecognitionResult.Status == SpeechRecognitionResultStatus.Success)
                {
                    HandleRecognitionResult(speechRecognitionResult);
                }
                else
                {
                    tbEstadoReconocimiento.Visibility = Visibility.Visible;
                    tbEstadoReconocimiento.Text = string.Format("Error de reconocimiento; estado: {0}", speechRecognitionResult.Status.ToString());
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
        }

        private void HandleRecognitionResult (SpeechRecognitionResult ecoResult)
        {
            if (ecoResult.Text.ToString() != "") //si reconoce algo con texto, sea cual sea
            {
                tbTextoReconocido.Text = ecoResult.Text; //presentamos el resultado

                //lo interpretamos
                if (ecoResult.SemanticInterpretation.Properties.ContainsKey("QUE_HORA"))
                {
                    tbDiccionario.Text = ecoResult.SemanticInterpretation.Properties["QUE_HORA"][0].ToString();
                }
                if (ecoResult.SemanticInterpretation.Properties.ContainsKey("QUE_DIA"))
                {
                    tbDiccionario.Text = ecoResult.SemanticInterpretation.Properties["QUE_DIA"][0].ToString();
                }
                if (ecoResult.SemanticInterpretation.Properties.ContainsKey("consulta"))
                {
                    tbDiccionario.Text = ecoResult.SemanticInterpretation.Properties["consulta"][0].ToString();
                }


                // anulamos el objeto de resultado para que no vuelva a entrar en el bucle, e invocamos el reconocimiento de nuevo
                ecoResult = null;
                reconocerContinuamente();
            }
            else { //si no devuelve texto, probablemente hubiera un silencio; volvemos a invocar
                reconocerContinuamente();
            }
                        
        }

        private void Button_Click(object sender, RoutedEventArgs e)
        {   //TODO: lanzar el reconocimiento manualmente, una vez concluya el automático; A DESAPARECER
            reconocerContinuamente();
        }
    }
}
