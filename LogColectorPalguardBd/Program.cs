using MySql.Data.MySqlClient;
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
        static string statusRede = "";
        static FileSystemWatcher watcher;
        static bool firstRead = true;


        static void Main(string[] args)
        {
            InicializarEnv();
            InicializarWatcher();
            IniciarMonitoramento();
            Console.ReadLine();
        }
        static void InicializarEnv()
        {
            pastaMonitorada = Environment.GetEnvironmentVariable("PASTA_MONITORADA") ?? @"\\OPTSUKE01\palguard\logs";
            conexaoBD = Environment.GetEnvironmentVariable("CONEXAO_BD") ?? "Server=192.168.100.84;Database=db-palworld-pvp-insiderhub;Uid=PalAdm;Pwd=sukelord;";
            ultimaPosicao = long.TryParse(Environment.GetEnvironmentVariable("ULTIMA_POSICAO"), out var posicao) ? posicao : 4589;
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
                        InserirNoBancoAsync(new List<string> { penultimaLinha }).Wait();
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
                            ultimaPosicao = sr.BaseStream.Position;
                        }
                    }

                    if (linhas.Count > 0)
                    {
                        await InserirNoBancoAsync(linhas);
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

                            //Console.WriteLine($"[{DateTime.Now}] Query executada: {query.Replace("@messageLog", linha)}");
                            Console.WriteLine($"[{DateTime.Now}] Linha inserida: {linha}");
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[{DateTime.Now}] Erro ao inserir no banco: {ex.Message}");
            }
        }
    }
}
