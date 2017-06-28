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
        private Boolean bolTomandoNota; //para saber si está tomando nota  //TODO: A DEPRECAR
        private enum Estado { Parado, ReconociendoContinuamente, TomandoNota};
        private enum SiguienteAccion { Parado, ReconocerContinuamente, TomarNota };
        private Estado miEstado;
        private SiguienteAccion nextStep; 

        private StringBuilder szTextoDictado; //el texto que recoges

        public MainPage()
        {
            this.InitializeComponent();
        }

        protected async override void OnNavigatedTo(NavigationEventArgs e) //cuando llegas
        {
            bolTomandoNota = false; //iniciamos sin el reconocedor continuo  //TODO: A DEPRECAR
            
            miEstado = Estado.Parado; //iniciamos parados
                
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
                await InitializeRecognizer(speechLanguage);
              await InitializeTomaNota(speechLanguage);

                //// y lanza EL TOMA NOTA, para saber cuándo me hace la llamada
               TomaNota();

            }
            else
            {
                tbEstadoReconocimiento.Visibility = Visibility.Visible;
                tbEstadoReconocimiento.Text = "Sin acceso al micrófono";
            }
        }


        #region GestionDelHabla

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
            catch (Exception e)
            {
                var msg = new Windows.UI.Popups.MessageDialog(e.Message, "Error hablando:");
                await msg.ShowAsync();
            }
        }
        #endregion



        #region ReconocimientoContinuo
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
                  //  tbEstadoReconocimiento.Visibility = Visibility.Visible;
//                    tbEstadoReconocimiento.Text = "Gramática compilada, reconociendo";                   
                        //me casca cada vez que vuelvo de tomar nota

                    speechRecognizer.Timeouts.EndSilenceTimeout = TimeSpan.FromSeconds(1.2);//damos tiempo a hablar

                }
            }
            catch (Exception e)
            {
                var messageDialog = new Windows.UI.Popups.MessageDialog(e.Message, "Excepción chunga: ");
                await messageDialog.ShowAsync();
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

        public async void ParaDeReconocerContinuamente()
        {
            try
            {
                if (speechRecognizer != null)
                {
                    if (speechRecognizer.State != SpeechRecognizerState.Idle)
                    {
                        if (recognitionOperation != null)
                        {
                            recognitionOperation.Cancel();
                            recognitionOperation = null;

                            bolTomandoNota = false;
                            nextStep = SiguienteAccion.Parado;
                            this.tbEstadoReconocimiento.Text = "Reco continuo parado";
                        }
                    }
                    else
                    {
                        //ParaDeReconocerContinuamente(); //lo intento hasta que salga del estado idle TIENE PINTA DE ESTAR MAL
                    }

                    speechRecognizer.StateChanged -= SpeechRecognizer_StateChanged;

                    this.speechRecognizer.Dispose();
                    this.speechRecognizer = null;
                }              
            }
            catch (Exception)
            {
                await dispatcher.RunAsync(CoreDispatcherPriority.Normal, () =>
                {
                    tbEstadoReconocimiento.Text = "Problema deteniendo el reconocimiento continuo: ";                    
                });

            }
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

        private async  Task  interpretaResultado(SpeechRecognitionResult recoResult)
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
                    }
                    if (recoResult.SemanticInterpretation.Properties.ContainsKey("orden"))
                    {
                        tbDiccionario.Text = recoResult.SemanticInterpretation.Properties["orden"][0].ToString();
                        await dime(RespondeALaComunicacion(recoResult.SemanticInterpretation.Properties["orden"][0].ToString()));
                    if (recoResult.SemanticInterpretation.Properties["orden"][0].ToString() == "TOMANOTA")
                    {
                        nextStep = SiguienteAccion.TomarNota; //pidió tomar nota: el siguiente paso será tomar nota, pues (ver más abajo en qué repercute)
                    }
                }

                // si pidió tomar nota, salimos de aquí; en caso contrario, anulamos el objeto de resultado para que no vuelva a entrar en el bucle, e invocamos el reconocimiento de nuevo
                if (nextStep == SiguienteAccion.ReconocerContinuamente)
                {
                    recoResult = null;
                    reconocerContinuamente();
                }
                else {
                    ParaDeReconocerContinuamente();                  
                    TomaNota();
                } //salir de aquí
            }
            else
            { //si no devuelve texto, probablemente hubiera un silencio; volvemos a invocar
                reconocerContinuamente();
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
                    return "Vamos a tomar nota";
                case "BULTO":
                    return "Vamos a crear un bulto";
                default:
                    return "No sé gestionar tu mensaje";
            }
        }
#endregion


        #region TomaNota
        private async Task InitializeTomaNota(Language recognizerLanguage)
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

        private async void TomaNota()
        {
            //if (bolTomandoNota == false)
            if (miEstado != Estado.TomandoNota)
            {
                if (speechRecognizerNotas.State == SpeechRecognizerState.Idle)
                {                  

                    try
                    {
                        szTextoDictado = new StringBuilder();
                        //bolTomandoNota = true;
                        miEstado = Estado.TomandoNota;
                        await speechRecognizerNotas.ContinuousRecognitionSession.StartAsync();
                    }
                    catch (Exception ex)
                    {
                       
                        var messageDialog = new Windows.UI.Popups.MessageDialog(ex.Message, "Exception");
                           await messageDialog.ShowAsync();

                        //bolTomandoNota = false;     
                        miEstado = Estado.Parado;

                    }
                }
                else
                {
                    // bolTomandoNota = false; 
                    miEstado = Estado.Parado;

                    if (speechRecognizerNotas.State != SpeechRecognizerState.Idle)
                    {
                        // hacemos stopAsync para dejar que acabe, ya que no está en reposo...
                        try
                        {
                            await speechRecognizerNotas.ContinuousRecognitionSession.StopAsync();

                            // mostramos lo último entendido
                            this.tbTextoReconocido.Text = szTextoDictado.ToString();
                        }
                        catch (Exception exception)
                        {
                            var messageDialog = new Windows.UI.Popups.MessageDialog(exception.Message, "Exception");
                            await messageDialog.ShowAsync();
                        }
                    }
                }

            }
        }

        private async void ParaTomaNota()
        {
            if (this.speechRecognizerNotas != null)
            {
                if (this.speechRecognizerNotas.State != SpeechRecognizerState.Idle)
                {
                    await speechRecognizerNotas.ContinuousRecognitionSession.StopAsync();

                    //paramos el reconocimiento
                    //await speechRecognizerNotas.ContinuousRecognitionSession.CancelAsync();

                    //y actualizamos el estado, y el siguiente paso
                    //  bolTomandoNota = false;
                    miEstado = Estado.Parado;
                    nextStep = SiguienteAccion.ReconocerContinuamente;

                    //eliminamos los objetos
                    speechRecognizerNotas.StateChanged -= SpeechRecognizer_StateChanged;
                    speechRecognizerNotas.ContinuousRecognitionSession.Completed -= ContinuousRecognitionSession_Completed;
                    speechRecognizerNotas.ContinuousRecognitionSession.ResultGenerated -= ContinuousRecognitionSession_ResultGenerated;
                    speechRecognizerNotas.HypothesisGenerated -= SpeechRecognizer_HypothesisGenerated;

                    this.speechRecognizerNotas.Dispose();
                    this.speechRecognizerNotas = null;

                    bolTomandoNota = false;
                    nextStep = SiguienteAccion.Parado;
                    this.tbEstadoReconocimiento.Text = "Captura de nota parada";

                    /////////////////////////////////////////////////////////////////////////////////////////////
                    /* No ejecutamos por ahora este código, hasta saber si va a funcionar bien o no; lo dejamos
                     *  parado, para invocar con un nuevo botón
                    //y volvemos a llamar al reconocimiento continuo, con su lenguaje, inicialización, ...
                    //escoge castellano (válido para todos los reconocedores)
                    Language speechLanguage = SpeechRecognizer.SystemSpeechLanguage;

                    // inicializo los dos reconocedores (el de gramática compilada, y el contínuo de las notas)                
                    await InitializeRecognizer(speechLanguage);
                    reconocerContinuamente();
                    */////////////////////////////////////////////////////////////////////////////////////////////
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
                        // bolTomandoNota = false;
                        miEstado = Estado.Parado;
                    });
                }
                else
                {
                    await dispatcher.RunAsync(CoreDispatcherPriority.Normal, () =>
                    {
                        this.tbEstadoReconocimiento.Text = "Reconocimiento exitoso";
                        this.tbxConsola.Text = args.Status.ToString();                        
                        this.btnRecoLibre.Content = "Reconocimiento libre";
                        //bolTomandoNota = true;
                        miEstado = Estado.TomandoNota;
                        
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


                if (szTextoDictado.ToString().Contains("Final nota"))
                {
                    //bolTomandoNota = false;
                    miEstado = Estado.Parado;
                    limpiaFormulario();
                    ParaTomaNota();
                }
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
        #endregion

        #region ElementosVisuales

        private async void limpiaFormulario()
        {
            await dispatcher.RunAsync(CoreDispatcherPriority.Normal, () =>
            {
                this.tbDiccionario.Text = "";
                this.tbTextoReconocido.Text = "";
                this.tbConfianza.Text = "";
                tbxConsola.SelectAll();
                tbxConsola.SelectedText = "";
            });

        } //limpia el formulario entre ejecuciones

        private async void  Button_Click(object sender, RoutedEventArgs e)
        {
            Language speechLanguage = SpeechRecognizer.SystemSpeechLanguage;
            await InitializeRecognizer(speechLanguage);
            reconocerContinuamente();
        }

        private void  BtnRecoLibre_Click(object sender, RoutedEventArgs e)
        {
            if (bolTomandoNota == true)
            {
             //TODO

            }
            else
            {
                limpiaFormulario();
                TomaNota();
            }
        }
        #endregion


        #region ControlesManualesTemporales
        private void  BtnParaRecoContinuo(object sender, RoutedEventArgs e)
        {
            ParaDeReconocerContinuamente();

            bolTomandoNota = false;
            nextStep = SiguienteAccion.Parado;
            this.tbEstadoReconocimiento.Text = "Reco continuo parado";
        }

        private void BtnParaTomaNota(object sender, RoutedEventArgs e)
        {
            ParaTomaNota();

            bolTomandoNota = false;
            nextStep = SiguienteAccion.Parado;
            this.tbEstadoReconocimiento.Text = "Captura de nota parada";
        }

        private async void BtnLanzaRecoContinuo(object sender, RoutedEventArgs e)
        {      
            //inicializamos, y lanzamos el reconocimiento
            Language speechLanguage = SpeechRecognizer.SystemSpeechLanguage;
            await InitializeRecognizer(speechLanguage);
            reconocerContinuamente();

            //actualizamos el estado          
            nextStep = SiguienteAccion.ReconocerContinuamente;

            this.tbEstadoReconocimiento.Text = "A reconocer continuamente";
        }

        private async void BtnLanzaTomaNota(object sender, RoutedEventArgs e)
        {
            //inicializamos, y lanzamos            
            Language speechLanguage = SpeechRecognizer.SystemSpeechLanguage;
            await InitializeTomaNota(speechLanguage);
            TomaNota();

            //actualizamos el estado            
            nextStep = SiguienteAccion.TomarNota;

            this.tbEstadoReconocimiento.Text = "A tomar nota";
        }
        #endregion
    }
}
