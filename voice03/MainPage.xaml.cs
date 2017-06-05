using System.Threading.Tasks;
using Windows.Storage;
using Windows.Media.SpeechRecognition;
using Windows.UI.Core;
using Windows.ApplicationModel;
using Windows.ApplicationModel.Resources.Core;
using Windows.Globalization;
using Windows.Media.SpeechSynthesis;
using System.Diagnostics;
using System.Text;

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
        private SpeechRecognizer speechRecognizerNotas;
        private IAsyncOperation<SpeechRecognitionResult> recognitionOperation;
        private CoreDispatcher dispatcher;
        private SpeechSynthesizer synthesizer;
        private SpeechRecognitionResult speechRecognitionResult; 
        private Boolean bolTomandoNota; //para saber si está tomando nota
        private StringBuilder szTextoDictado; //el texto que recoges

        public MainPage()
        {
            this.InitializeComponent();
        }

        protected async override void OnNavigatedTo(NavigationEventArgs e) //cuando llegas
        {
            bolTomandoNota = false; //iniciamos sin el reconocedor continuo
            dispatcher = CoreWindow.GetForCurrentThread().Dispatcher;

            //comprobación de si tengo permiso sobre el micrófono; si tengo, inicio el proceso (InitializeRecognizer)
            bool tengoPermiso = await AudioCapturePermissions.RequestMicrophonePermission();
            if (tengoPermiso)
            {
                // lanza el habla 
                inicializaHabla();        
          
                //escoge castellano (válido para todos los reconocedores)
                Language speechLanguage = SpeechRecognizer.SystemSpeechLanguage;

                // inicializo los dos reconocedores (el de gramática compilada, y el contínuo de las notas)                
                //await InitializeRecognizer(speechLanguage);
                await InitializeTomaNota(speechLanguage);

                //// y lanza el reconocimiento contínuo (TODO: ahora no lo hago para probar el otro)
                //reconocerContinuamente();

            }
            else
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
                //cargar el fichero de la gramática
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

        private async Task InitializeTomaNota (Language recognizerLanguage)
        {
            if (speechRecognizerNotas != null)
            {
                //si vengo de una ejecución anterior, hacemos limpieza
                speechRecognizerNotas.StateChanged -= SpeechRecognizer_StateChanged;
                speechRecognizerNotas.ContinuousRecognitionSession.Completed -= ContinuousRecognitionSession_Completed;
                speechRecognizerNotas.ContinuousRecognitionSession.ResultGenerated -= ContinuousRecognitionSession_ResultGenerated;
                speechRecognizerNotas.HypothesisGenerated -= SpeechRecognizer_HypothesisGenerated;

                this.speechRecognizerNotas.Dispose();
                this.speechRecognizerNotas = null;
            }

            this.speechRecognizerNotas = new SpeechRecognizer(recognizerLanguage);

            speechRecognizerNotas.StateChanged += SpeechRecognizer_StateChanged; //feedback al usuario

            // en vez de gramática, aplicamos el caso de uso "Dictado"
            var dictationConstraint = new SpeechRecognitionTopicConstraint(SpeechRecognitionScenario.Dictation, "dictation");
            speechRecognizerNotas.Constraints.Add(dictationConstraint);
            SpeechRecognitionCompilationResult result = await speechRecognizerNotas.CompileConstraintsAsync();
            if (result.Status != SpeechRecognitionResultStatus.Success)
            {                
                var messageDialog = new Windows.UI.Popups.MessageDialog(result.Status.ToString(), "Excepción chunga: ");
                await messageDialog.ShowAsync();
            }

            // nos registramos a los eventos
            speechRecognizerNotas.ContinuousRecognitionSession.Completed += ContinuousRecognitionSession_Completed; //no hubo éxito
            speechRecognizerNotas.ContinuousRecognitionSession.ResultGenerated += ContinuousRecognitionSession_ResultGenerated; //o entendió, o llegó basura
            speechRecognizerNotas.HypothesisGenerated += SpeechRecognizer_HypothesisGenerated; //se va alimentando de lo que va llegando para dar feedback
        }

        private async void SpeechRecognizer_StateChanged(SpeechRecognizer sender, SpeechRecognizerStateChangedEventArgs args)
        {//feedback al usuario del estado del reconocimiento de texto
            await dispatcher.RunAsync(CoreDispatcherPriority.Normal, () =>
            {
                tbEstadoReconocimiento.Text = "Estado de reconocimiento de texto: " + args.State.ToString();                
                tbxConsola.Text += args.State.ToString() + Environment.NewLine;
            });

                if (args.State == SpeechRecognizerState.SoundEnded)
                {                
                //TODO: mostrar las palabras escuchadas    
            }        
        }       


        public async void reconocerContinuamente()
        {
            try
            {            
                
                recognitionOperation = speechRecognizer.RecognizeAsync(); //y utilizamos éste, que no muestra el pop-up
                                                                        
                speechRecognitionResult = await recognitionOperation;

                if (speechRecognitionResult.RawConfidence < 0.5 && speechRecognitionResult.Text != "") //si no ha entendido, pero ha escuchado algo
                {
                    await dime("Creo que no te he entendido");                    
                    limpiaFormulario();
                    reconocerContinuamente();
                }
                 
                else 

                if (speechRecognitionResult.Status == SpeechRecognitionResultStatus.Success) //si por el contrario hay match entre gramática y lo escuchado
                {   
                        await interpretaResultado(speechRecognitionResult);
                 
                }
                else //si no ha escuchado nada
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

        private async Task interpretaResultado (SpeechRecognitionResult recoResult)
        {
            limpiaFormulario(); // limpiamos del formulario la intepretación anterior;

            if (recoResult.Text.ToString() != "") //si reconoce algo con texto, sea cual sea
            {
                if (recoResult.Status != SpeechRecognitionResultStatus.Success)
                {
                    await dime("Creo que no te he entendido bien");        
                }
                else
                {
                    tbTextoReconocido.Text = recoResult.Text; //presentamos el resultado
                    tbConfianza.Text = recoResult.RawConfidence.ToString(); //presentamos la confianza

                    //lo interpretamos
                    if (recoResult.SemanticInterpretation.Properties.ContainsKey("consulta"))
                    {
                        tbDiccionario.Text = recoResult.SemanticInterpretation.Properties["consulta"][0].ToString();                    
                        await dime(RespondeALaComunicacion(recoResult.SemanticInterpretation.Properties["consulta"][0].ToString()));             
                    }
                    if (recoResult.SemanticInterpretation.Properties.ContainsKey("orden"))
                    {
                        tbDiccionario.Text = recoResult.SemanticInterpretation.Properties["orden"][0].ToString();
                        await dime(RespondeALaComunicacion(recoResult.SemanticInterpretation.Properties["orden"][0].ToString()));
                    }

                }

                // anulamos el objeto de resultado para que no vuelva a entrar en el bucle, e invocamos el reconocimiento de nuevo
                recoResult = null;
                reconocerContinuamente();
            }
            else { //si no devuelve texto, probablemente hubiera un silencio; volvemos a invocar
                reconocerContinuamente();
            }
                        
        }

        private void limpiaFormulario()
        {
            this.tbDiccionario.Text = "";
            this.tbTextoReconocido.Text = "";
            this.tbConfianza.Text = "";
            tbxConsola.SelectAll();
            tbxConsola.SelectedText = "";
        }

        private void inicializaHabla()
        {
            //lanza el habla
            synthesizer = new SpeechSynthesizer();

            VoiceInformation voiceInfo =
         (
           from voice in SpeechSynthesizer.AllVoices
           where voice.Gender == VoiceGender.Female
           select voice
         ).FirstOrDefault() ?? SpeechSynthesizer.DefaultVoice;

            synthesizer.Voice = voiceInfo;
        }

        private async Task dime(String szTexto)
        {
            try
            {
                // crear el flujo desde el texto
                SpeechSynthesisStream synthesisStream = await synthesizer.SynthesizeTextToStreamAsync(szTexto);

                // ...y lo dice
                media.AutoPlay = true;
                media.SetSource(synthesisStream, synthesisStream.ContentType);
                media.Play();
            }
            catch (Exception e) {
                var msg = new Windows.UI.Popups.MessageDialog(e.Message, "Error hablando:");
                await msg.ShowAsync();
            }
        }

        private string RespondeALaComunicacion(string szMensaje)
        {
            switch (szMensaje)
            {
                case "HORA":
                    return DateTime.Now.ToString("h:mm");
                case "DIA":
                    return DateTime.Now.ToString("dd") + " de " + DateTime.Now.ToString("MMMM");
                case "TOMANOTA":
                    return "Vamos a tonar nota";
                default:
                    return "No sé gestionar tu mensaje";                    
            }
        }


        private async void TomaNota()
        {
           if (bolTomandoNota == false)
            {
                if (speechRecognizerNotas.State == SpeechRecognizerState.Idle)
                {                  

                    try
                    {
                        szTextoDictado = new StringBuilder();
                        bolTomandoNota = true;
                        await speechRecognizerNotas.ContinuousRecognitionSession.StartAsync();
                    }
                    catch (Exception ex)
                    {
                       
                        var messageDialog = new Windows.UI.Popups.MessageDialog(ex.Message, "Exception");
                           await messageDialog.ShowAsync();                       

                        bolTomandoNota = false;     

                    }
                }
            }

           
           
        }

        private async void ContinuousRecognitionSession_Completed(SpeechContinuousRecognitionSession sender, SpeechContinuousRecognitionCompletedEventArgs args)
        {
            if (args.Status != SpeechRecognitionResultStatus.Success)
            {
                // durante 20 segundos, ha estado callado...                
                if (args.Status == SpeechRecognitionResultStatus.TimeoutExceeded)
                {
                    await dispatcher.RunAsync(CoreDispatcherPriority.Normal, () =>
                    {
                        this.tbEstadoReconocimiento.Text = "Te has pasado de tiempo";                        
                        this.btnRecoLibre.Content = "Reconocimiento libre";
                        bolTomandoNota = false;
                    });
                }
                else
                {
                    await dispatcher.RunAsync(CoreDispatcherPriority.Normal, () =>
                    {
                        this.tbEstadoReconocimiento.Text = "Reconocimiento exitoso";
                        this.tbxConsola.Text = args.Status.ToString();                        
                        this.btnRecoLibre.Content = "Reconocimiento libre";                         
                        bolTomandoNota = true;
                    });
                }
            }
        }

        private async void ContinuousRecognitionSession_ResultGenerated(SpeechContinuousRecognitionSession sender, SpeechContinuousRecognitionResultGeneratedEventArgs args)
        { //se han generado resultados, y si la confianza es buena, los presento
            // Caso de que la confianza es buena en lo que ha entendido            
            if (args.Result.Confidence == SpeechRecognitionConfidence.Medium ||
                args.Result.Confidence == SpeechRecognitionConfidence.High)
            {
                szTextoDictado.Append(args.Result.Text + " ");

                await dispatcher.RunAsync(CoreDispatcherPriority.Normal, () =>
                {                    
                    tbTextoReconocido.Text = szTextoDictado.ToString();                    
                });
            }
            else
            {
               //caso de que la confianza en lo identificado sea baja
                await dispatcher.RunAsync(CoreDispatcherPriority.Normal, () =>
                {
                    tbTextoReconocido.Text = szTextoDictado.ToString();
                    string discardedText = args.Result.Text;
                    if (!string.IsNullOrEmpty(discardedText))
                    {
                        discardedText = discardedText.Length <= 25 ? discardedText : (discardedText.Substring(0, 25) + "...");

                        this.tbxConsola.Text = "Descartado por baja confianza: " + discardedText;                        
                    }
                });
            }
        }

        private async void SpeechRecognizer_HypothesisGenerated(SpeechRecognizer sender, SpeechRecognitionHypothesisGeneratedEventArgs args)
        {//según va entendiendo, ir mostrando la información en la UI
            string hypothesis = args.Hypothesis.Text;

            // Update the textbox with the currently confirmed text, and the hypothesis combined.
            string textboxContent = szTextoDictado.ToString() + " " + hypothesis + " ...";
            await dispatcher.RunAsync(CoreDispatcherPriority.Normal, () =>
            {
                tbTextoReconocido.Text = textboxContent;                
            });
        }

        private void Button_Click(object sender, RoutedEventArgs e)
        {   //TODO: lanzar el reconocimiento manualmente, una vez concluya el automático; A DESAPARECER
            reconocerContinuamente();
        }

        private void BtnRecoLibre_Click(object sender, RoutedEventArgs e)
        {
            if (bolTomandoNota == true)
            {
                //ya viene de vuelta, y quiere parar el reconocimiento
                //TODO
            }
            else
            {
                limpiaFormulario();
                TomaNota();
            }
        }

    }
}
