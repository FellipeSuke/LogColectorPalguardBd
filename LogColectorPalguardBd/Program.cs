using MySql.Data.MySqlClient;
using System.Collections.Concurrent;
using System.Text;
using System.Timers;

namespace MonitorLog
{
    class Program
    {
        static string pastaMonitorada;
        static string arquivoLog = "";
        static string arquivoLastLog = "";
        static string conexaoBD;
        static long ultimaPosicao;
        static int quantidadeReinicio = 0;
        static int logDesatualizado = 0;
        static int tempoDeLogDesatualizado;
        static string statusRede = "";
        static FileSystemWatcher watcher;
        static bool firstRead = true;
        static (int Left, int Top) cursorPosition;
        static string apiUrl = "http://201.14.75.202:8212/v1/api/info";
        static string username = "admin";
        static string password = "unreal";


        static void Main(string[] args)
        {
            InicializarEnv();
            Console.WriteLine("LogMonitorPalguardDb Version 1.0.0");
            int contagemTempoLog = CalcularContagem(tempoDeLogDesatualizado);
            Console.WriteLine(contagemTempoLog + " Vezes Maxima.");
            DateTime inicio = DateTime.Now;
            InicializarWatcher();
            IniciarMonitoramento();

            //Console.WriteLine("Pressione 'q' e Enter para sair...");
            while (true)
            {
                

                if (logDesatualizado >= contagemTempoLog)
                {
                    
                    var verificaServidorTask = VerificaServidor();
                    verificaServidorTask.Wait();  // Aguarda a conclusão da tarefa
                    bool servidorOk = verificaServidorTask.Result;


                    if (!servidorOk)
                    {
                        Console.WriteLine("Servidor OffLine, encerrando app");
                        Console.WriteLine(DateTime.Now - inicio);
                        break;
                    }
                    else 
                    {
                        //Console.WriteLine(DateTime.Now - inicio);
                        //Console.WriteLine(DateTime.Now + " Servidor Online, aguardando logs");
                        logDesatualizado = 0;
                        //inicio = DateTime.Now;
                    }

                    

                }

            }

            // Se você tiver algum código para limpeza ou encerramento, coloque-o aqui.
            Console.WriteLine("Saindo...");
        }
        static void InicializarEnv()
        {
            pastaMonitorada = Environment.GetEnvironmentVariable("PASTA_MONITORADA") ?? @"\\OPTSUKE01\palguard\logs";
            conexaoBD = Environment.GetEnvironmentVariable("CONEXAO_BD") ?? "Server=192.168.100.84;Database=db-palworld-pvp-insiderhub;Uid=PalAdm;Pwd=sukelord;";
            ultimaPosicao = long.TryParse(Environment.GetEnvironmentVariable("ULTIMA_POSICAO"), out var posicao) ? posicao : 4589;
            tempoDeLogDesatualizado = int.TryParse(Environment.GetEnvironmentVariable("TEMPO_DE_LOG_DESATUALIZADO"), out var eLogDesatualizado) ? eLogDesatualizado : 30;

        }

        static void InicializarWatcher()
        {
            watcher = new FileSystemWatcher(pastaMonitorada)
            {
                NotifyFilter = NotifyFilters.LastWrite | NotifyFilters.FileName | NotifyFilters.CreationTime
            };
            watcher.Changed += OnChanged;
            watcher.Created += OnChanged;
            watcher.EnableRaisingEvents = true;
        }

        static void IniciarMonitoramento()
        {
            while (true)
            {
                try
                {
                    Console.WriteLine($"[{DateTime.Now}] Iniciando o monitoramento da pasta: {pastaMonitorada}");

                    ForcarLeituraArquivoMaisRecente();
                    ConfigurarTimer();

                    Console.WriteLine($"[{DateTime.Now}] Monitoramento iniciado. Aguardando alterações nos arquivos...");
                    break;
                }
                catch (Exception)
                {
                    Console.WriteLine($"[{DateTime.Now}] Rede indisponível. Tentando novamente...");
                    quantidadeReinicio++;
                    Thread.Sleep(60000);
                }
            }
        }

        static void ForcarLeituraArquivoMaisRecente()
        {
            try
            {
                DirectoryInfo dirInfo = new DirectoryInfo(pastaMonitorada);
                FileInfo arquivoMaisRecente = dirInfo.GetFiles()
                    .OrderByDescending(f => f.LastWriteTime)
                    .FirstOrDefault();

                if (arquivoMaisRecente != null)
                {
                    arquivoLog = arquivoMaisRecente.FullName;
                    if (arquivoLog != arquivoLastLog)
                    {
                        Console.WriteLine($"[{DateTime.Now}] Arquivo mais recente detectado: {arquivoMaisRecente.FullName}");
                        arquivoLastLog = arquivoLog;
                    }

                    if (firstRead)
                    {
                        firstRead = false;
                        LerPenultimaLinhaLog();
                    }
                    else
                    {
                        LerArquivoLog();
                    }

                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[{DateTime.Now}] Erro ao forçar a leitura do arquivo mais recente: {ex.Message}");
            }
        }

        static void LerPenultimaLinhaLog()
        {
            try
            {
                using (StreamReader sr = new StreamReader(new FileStream(arquivoLog, FileMode.Open, FileAccess.Read, FileShare.ReadWrite)))
                {
                    string penultimaLinha = null;
                    string linhaAtual = null;

                    while ((linhaAtual = sr.ReadLine()) != null)
                    {
                        if (!string.IsNullOrEmpty(linhaAtual))
                        {
                            penultimaLinha = linhaAtual;
                            ultimaPosicao = sr.BaseStream.Position;
                        }
                    }

                    if (penultimaLinha != null)
                    {
                        Console.WriteLine($"[{DateTime.Now}] Penúltima linha lida: {penultimaLinha}");
                        //InserirNoBancoAsync(new List<string> { penultimaLinha }).Wait();
                    }
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[{DateTime.Now}] Erro ao ler a penúltima linha do arquivo: {ex.Message}");
            }
        }

        static void OnChanged(object sender, FileSystemEventArgs e)
        {
            try
            {
                if (e.ChangeType == WatcherChangeTypes.Changed || e.ChangeType == WatcherChangeTypes.Created)
                {
                    Console.WriteLine($"[{DateTime.Now}] Arquivo detectado: {e.FullPath}");
                    arquivoLog = e.FullPath;
                    LerArquivoLog();
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[{DateTime.Now}] Erro ao processar alteração no arquivo: {ex.Message}");
            }
        }

        static void ConfigurarTimer()
        {
            System.Timers.Timer timer = new System.Timers.Timer(100);
            timer.Elapsed += OnTimedEvent;
            timer.Start();
        }

        static void OnTimedEvent(object source, ElapsedEventArgs e)
        {
            try
            {
                if (!string.IsNullOrEmpty(arquivoLog))
                {
                    LerArquivoLog();
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[{DateTime.Now}] Erro no evento de tempo: {ex.Message}");
            }
        }

        static async void LerArquivoLog()
        {
            try
            {
                using (StreamReader sr = new StreamReader(new FileStream(arquivoLog, FileMode.Open, FileAccess.Read, FileShare.ReadWrite)))
                {
                    sr.BaseStream.Seek(ultimaPosicao, SeekOrigin.Begin);

                    if (statusRede == "REDE INDISPONIVEL")
                    {
                        statusRede = "REDE DISPONIVEL";
                        Thread.Sleep(30000);
                        Console.WriteLine("############# REDE DISPONIVEL #############");
                        ultimaPosicao = 4589;
                        ForcarLeituraArquivoMaisRecente();
                    }

                    List<string> linhas = new List<string>();
                    string linha;

                    while ((linha = sr.ReadLine()) != null)
                    {
                        if (!string.IsNullOrWhiteSpace(linha))
                        {
                            //Console.WriteLine($"[{DateTime.Now}] Linha lida: {linha}");
                            linhas.Add(linha);
                            logDesatualizado = 0;
                            ultimaPosicao = sr.BaseStream.Position;

                        }
                    }

                    if (linhas.Count > 0)
                    {
                        await InserirNoBancoAsync(linhas);
                    }
                    else
                    {
                        logDesatualizado++;
                    }
                }
            }
            catch (Exception ex)
            {
                if (statusRede != "REDE INDISPONIVEL")
                {
                    statusRede = "REDE INDISPONIVEL";
                    Console.WriteLine("############# REDE INDISPONIVEL #############");
                    Console.WriteLine($"[{DateTime.Now}] Erro ao ler o arquivo: {ex.Message}");
                }
                Thread.Sleep(60000);
            }
        }

        static async Task InserirNoBancoAsync(List<string> linhas)
        {
            try
            {
                using (MySqlConnection conexao = new MySqlConnection(conexaoBD))
                {
                    await conexao.OpenAsync();

                    string query = "INSERT INTO logger_data (messageLog) VALUES (@messageLog)";
                    using (MySqlCommand cmd = new MySqlCommand(query, conexao))
                    {
                        foreach (string linha in linhas)
                        {
                            cmd.Parameters.Clear();
                            cmd.Parameters.AddWithValue("@messageLog", linha);
                            await cmd.ExecuteNonQueryAsync();

                            Console.WriteLine($"[{DateTime.Now}] Linha inserida: {linha}");
                        }
                    }
                }
            }
            catch (MySqlException ex)
            {
                Console.WriteLine($"[{DateTime.Now}] Erro ao inserir no banco de dados: {ex.Message}");
                Console.WriteLine($"Código de erro: {ex.Number}");
                Console.WriteLine($"Detalhes: {ex.StackTrace}");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[{DateTime.Now}] Erro geral ao inserir no banco de dados: {ex.Message}");
                Console.WriteLine($"Detalhes: {ex.StackTrace}");
            }
        }

        static int CalcularContagem(int minutos)
        {
            // Taxa de incremento por segundo baseada no cálculo anterior
            double incrementosPorSegundo = 1;
            int segundos = minutos * 10;
            int contagem = (int)(incrementosPorSegundo * segundos);
            return contagem;
        }

        public static async Task<bool> VerificaServidor() 
        {
            try
            {
                var responseContent = await CallApiWithBasicAuth(apiUrl, username, password);
                //Console.WriteLine("Resposta da API:");
                //Console.WriteLine(responseContent);
                return true;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Ocorreu um erro: {ex.Message}");
                return false;
            }
        }
        static async Task<string> CallApiWithBasicAuth(string apiUrl, string username, string password)
        {
            using (var client = new HttpClient())
            {
                var credentials = $"{username}:{password}";
                var base64Credentials = Convert.ToBase64String(Encoding.ASCII.GetBytes(credentials));
                var authorizationHeader = $"Basic {base64Credentials}";

                var request = new HttpRequestMessage(HttpMethod.Get, apiUrl);
                request.Headers.Add("Authorization", authorizationHeader);

                try
                {
                    var response = await client.SendAsync(request);
                    response.EnsureSuccessStatusCode();
                    return await response.Content.ReadAsStringAsync();
                }
                catch (HttpRequestException ex)
                {
                    // Lida com erros específicos de requisição HTTP
                    Console.WriteLine($"Erro na requisição HTTP: {ex.Message}");
                    throw;
                }
                catch (Exception ex)
                {
                    // Lida com erros gerais
                    Console.WriteLine($"Erro geral: {ex.Message}");
                    throw;
                }
            }
        }

    }
}
