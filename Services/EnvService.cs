namespace RobloxUpdateBot.Services
{
    internal class EnvService
    {
        internal EnvService()
        {
            if (!File.Exists($"{AppDomain.CurrentDomain.BaseDirectory}.env")) return;
            string[] lines = File.ReadAllLines($"{AppDomain.CurrentDomain.BaseDirectory}.env");
            foreach (string line in lines)
            {
                string[] parts = line.Split('=');
                if (parts.Length != 2) continue;
                string key = parts[0].Trim();
                string value = parts[1].Trim();

                if (string.IsNullOrEmpty(Environment.GetEnvironmentVariable(key)))
                    Environment.SetEnvironmentVariable(key, value);
            }
        }
    }
}
