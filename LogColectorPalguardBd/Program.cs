using Google.Protobuf.WellKnownTypes;
using MySql.Data.MySqlClient;
using Org.BouncyCastle.Asn1.Cmp;
using System.Text;
using System.Timers;

namespace MonitorLog
{
    class Program
    {
        static string pastaMonitorada;
        static string arquivoLog;
        static string conexaoBD;
        static long ultimaPosicao;
        static int logDesatualizado = 0;
        static int tempoDeLogDesatualizado;
        static string apiUrl = "http://201.14.75.202:8212/v1/api/info";
        static string username = "admin";
        static string password = "unreal";
        static System.Timers.Timer monitorTimer;
        static int numeroDeReconexao = 0;
        static int reconexaoLogger;

        static void Main(string[] args)
        {
            Console.WriteLine("Entrando no método Main.");
            string versionApp = "2.0.0";
            InicializarEnv();
            List<string> iniciaApp = new List<string>();
            iniciaApp.Add($"LogMonitorPalguardDb Iniciado Version "+ versionApp);
            InserirNoBancoAsync(iniciaApp, "LogMonitorPalguardDb");
            Console.WriteLine(iniciaApp[0]);
            IniciarMonitoramento();

            Console.WriteLine("Pressione 'Ctrl + C' para sair...");

            // Loop infinito para manter o aplicativo em execução
            while (true)
            {
                Thread.Sleep(1000); // Aguarda 1 segundo
            }
        }

        static void InicializarEnv()
        {
            Console.WriteLine("Entrando no método InicializarEnv.");

            pastaMonitorada = Environment.GetEnvironmentVariable("PASTA_MONITORADA") ?? @"\\OPTSUKE01\palguard\logs";
            conexaoBD = Environment.GetEnvironmentVariable("CONEXAO_BD") ?? "Server=192.168.100.84;Database=db-palworld-pvp-insiderhub;Uid=PalAdm;Pwd=sukelord;";
            ultimaPosicao = long.TryParse(Environment.GetEnvironmentVariable("ULTIMA_POSICAO"), out var posicao) ? posicao : 0;
            tempoDeLogDesatualizado = int.TryParse(Environment.GetEnvironmentVariable("TEMPO_DE_LOG_DESATUALIZADO"), out var eLogDesatualizado) ? eLogDesatualizado : 10;
            reconexaoLogger = int.TryParse(Environment.GetEnvironmentVariable("NUMERO_DE_RECONEXAO"), out var eReconexaoLogger) ? eReconexaoLogger : 10;

            Console.WriteLine($"Variáveis Inicializadas: pastaMonitorada={pastaMonitorada}, conexaoBD={conexaoBD}, ultimaPosicao={ultimaPosicao}, tempoDeLogDesatualizado={tempoDeLogDesatualizado}");
        }

        static void IniciarMonitoramento()
        {
            Console.WriteLine($"[{DateTime.Now}] Entrando no método IniciarMonitoramento.");
            Console.WriteLine($"[{DateTime.Now}] Iniciando o monitoramento da pasta: {pastaMonitorada}");

            ForcarLeituraArquivoMaisRecente();

            monitorTimer = new System.Timers.Timer(1000); // Verifica a cada 30 segundos
            monitorTimer.Elapsed += MonitorTimerElapsed;
            monitorTimer.Start();

            Console.WriteLine($"[{DateTime.Now}] Monitoramento iniciado. Aguardando alterações nos arquivos...");
        }

        static void MonitorTimerElapsed(object sender, ElapsedEventArgs e)
        {
            Console.WriteLine($"[{DateTime.Now}] MonitorTimerElapsed acionado. logDesatualizado={logDesatualizado}, tempoDeLogDesatualizado={tempoDeLogDesatualizado}");

            if (logDesatualizado >= tempoDeLogDesatualizado)
            {
                Console.WriteLine("logDesatualizado excedeu o tempo permitido. Verificando servidor...");
                VerificaServidor().Wait();
            }
            else
            {
                Console.WriteLine("Log atualizado. Continuando leitura do arquivo de log...");
                LerArquivoLog();
            }
        }

        static void ForcarLeituraArquivoMaisRecente()
        {
            Console.WriteLine("Entrando no método ForcarLeituraArquivoMaisRecente.");

            try
            {
                DirectoryInfo dirInfo = new DirectoryInfo(pastaMonitorada);
                FileInfo arquivoMaisRecente = dirInfo.GetFiles()
                    .OrderByDescending(f => f.LastWriteTime)
                    .FirstOrDefault();

                if (arquivoMaisRecente != null)
                {
                    arquivoLog = arquivoMaisRecente.FullName;
                    ultimaPosicao = 0;  // Reiniciar a leitura do início
                    Console.WriteLine($"[{DateTime.Now}] Arquivo mais recente detectado: {arquivoLog}");
                }
                else
                {
                    Console.WriteLine($"[{DateTime.Now}] Nenhum arquivo .log encontrado.");
                }

                LerArquivoLog();
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[{DateTime.Now}] Erro ao forçar a leitura do arquivo mais recente: {ex.Message}");
            }
        }

        static void LerArquivoLog()
        {
            Console.WriteLine($"Entrando no método LerArquivoLog. arquivoLog={arquivoLog}, ultimaPosicao={ultimaPosicao}");

            try
            {
                using (StreamReader sr = new StreamReader(new FileStream(arquivoLog, FileMode.Open, FileAccess.Read, FileShare.ReadWrite)))
                {
                    sr.BaseStream.Seek(ultimaPosicao, SeekOrigin.Begin);

                    List<string> linhas = new List<string>();
                    string linha;

                    while ((linha = sr.ReadLine()) != null)
                    {
                        logDesatualizado = 0;
                        numeroDeReconexao = 0;
                        Console.WriteLine(linha);
                        if (!string.IsNullOrWhiteSpace(linha) && !linha.Contains(" 'Info'") && !linha.Contains(" 'ShowPlayers'") && !linha.Contains(" [debug] Registered "))
                        {
                            linhas.Add(linha);
                        }
                    }

                    if (linhas.Count > 0)
                    {
                        ultimaPosicao = sr.BaseStream.Position;
                        Console.WriteLine($"[{DateTime.Now}] Novas linhas lidas: {linhas.Count}. Atualizando ultimaPosicao para {ultimaPosicao}");
                        InserirNoBancoAsync(linhas, Path.GetFileName(arquivoLog)).Wait();
                        logDesatualizado = 0;
                    }
                    else
                    {
                        ultimaPosicao = sr.BaseStream.Position;
                        Console.WriteLine($"[{DateTime.Now}] Nenhuma nova linha encontrada. Incrementando logDesatualizado.");
                        logDesatualizado++;
                    }
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[{DateTime.Now}] Erro ao ler o arquivo: {ex.Message}");
                logDesatualizado++;
            }
        }

        static async Task InserirNoBancoAsync(List<string> linhas, string nomeArquivo)
        {
            Console.WriteLine("Entrando no método InserirNoBancoAsync.");

            try
            {
                using (MySqlConnection conexao = new MySqlConnection(conexaoBD))
                {
                    await conexao.OpenAsync();
                    Console.WriteLine("Conexão ao banco de dados aberta.");

                    string query = "INSERT INTO logger_data (messageLog, logFileName) VALUES (@messageLog, @logFileName)";
                    using (MySqlCommand cmd = new MySqlCommand(query, conexao))
                    {
                        cmd.Parameters.AddWithValue("@logFileName", nomeArquivo); // Adiciona o parâmetro do nome do arquivo uma vez

                        foreach (string linha in linhas)
                        {
                            cmd.Parameters.Clear();
                            cmd.Parameters.AddWithValue("@messageLog", linha);
                            cmd.Parameters.AddWithValue("@logFileName", nomeArquivo); // Adiciona o nome do arquivo novamente
                            await cmd.ExecuteNonQueryAsync();
                            Console.WriteLine($"[{DateTime.Now}] Linha inserida no banco de dados: {linha}");
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[{DateTime.Now}] Erro ao inserir no banco de dados: {ex.Message}");
            }
        }


        static async Task VerificaServidor()
        {
            Console.WriteLine("Entrando no método VerificaServidor.");

            try
            {
                var responseContent = await CallApiWithBasicAuth(apiUrl, username, password);
                logDesatualizado = 0;
                numeroDeReconexao++;
                if (numeroDeReconexao > reconexaoLogger)
                {
                    List<string> errors = new List<string>();
                    errors.Add($"[{DateTime.Now}] Servidor offline ou erro ao verificar: Numero maximo de reconexões atingudo {numeroDeReconexao}. Encerrando monitoramento.");
                    InserirNoBancoAsync(errors, arquivoLog);
                    Console.WriteLine(errors[0]);
                    monitorTimer.Stop();
                    Environment.Exit(0);  // Terminar o aplicativo
                }
                Console.WriteLine("Servidor online. Monitoramento continuará. Numero de conexão " + numeroDeReconexao + "/" + reconexaoLogger);


            }
            catch (Exception ex)
            {
                List<string> errors = new List<string>();
                errors.Add($"[{DateTime.Now}] Servidor offline ou erro ao verificar: {ex.Message}. Encerrando monitoramento.");
                InserirNoBancoAsync(errors, arquivoLog);
                Console.WriteLine(errors[0]);
                monitorTimer.Stop();
                Environment.Exit(0);  // Terminar o aplicativo
            }
        }

        static async Task<string> CallApiWithBasicAuth(string apiUrl, string username, string password)
        {
            Console.WriteLine("Entrando no método CallApiWithBasicAuth.");

            using (var client = new HttpClient())
            {
                var credentials = $"{username}:{password}";
                var base64Credentials = Convert.ToBase64String(Encoding.ASCII.GetBytes(credentials));
                client.DefaultRequestHeaders.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Basic", base64Credentials);

                var response = await client.GetAsync(apiUrl);
                response.EnsureSuccessStatusCode();

                string responseContent = await response.Content.ReadAsStringAsync();
                Console.WriteLine($"Resposta da API recebida: {responseContent}");

                return responseContent;
            }
        }
    }
}
