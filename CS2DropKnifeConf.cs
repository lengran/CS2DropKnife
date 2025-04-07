using System.Text.Json;

namespace CS2DropKnife;

public class DropRules
{
    public bool DirectSend {get; set; } = true;

    public bool OncePerRound {get; set; } = true;

    public bool FreezeTimeOnly {get; set; } = true;

    public bool ChatFiltering {get; set; } = false;

    private string _moduleDirectory = "";

    public DropRules()
    {
        // DeserializeConstructor
    }

    public DropRules(string moduleDirectory)
    {
        _moduleDirectory = moduleDirectory;
    }

    public void LoadSettings()
    {
        string path = $"{_moduleDirectory}/settings.json";

        if (!File.Exists(path))
        {
            // Use default settings
            Console.WriteLine("[CS2DropKnife] No custom settings provided. Will use default settings.");
        }
        else
        {
            // Load settings from settings.json
            JsonSerializerOptions options = new JsonSerializerOptions
            {
                ReadCommentHandling = JsonCommentHandling.Skip,
                AllowTrailingCommas = true,

            };

            string jsonString = File.ReadAllText(path);
            
            try
            {
                DropRules jsonConfig = JsonSerializer.Deserialize<DropRules>(jsonString, options)!;

                DirectSend = jsonConfig.DirectSend;
                OncePerRound = jsonConfig.OncePerRound;
                FreezeTimeOnly = jsonConfig.FreezeTimeOnly;
                ChatFiltering = jsonConfig.ChatFiltering;

                Console.WriteLine($"[CS2DropKnife] Settings are loaded successfully.");
            }
            catch (System.Exception)
            {
                Console.WriteLine("[CS2DropKnife] Failed to load settings.json. Will use the default settings.");
            }
        }
    }
}