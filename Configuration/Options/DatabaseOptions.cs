using System.ComponentModel.DataAnnotations;

namespace Navislamia.Configuration.Options
{
    public class DatabaseOptions
    {
        [Required]
        public string DataSource { get; set; }

        [Required]
        public int Port { get; set; }

        [Required]
        public string User { get; set; }

        [Required]
        public string Password { get; set; }

        [Required]
        public string InitialCatalog { get; set; }

        public int? MaxPoolSize { get; set; }

        public int CommandTimeout { get; set; } = 30;

        public int CommandTimeoutMigration { get; set; } = 3600;
        
        public bool IncludeErrorDetail { get; set; }

        /// <summary>
        /// The resource database. Shared by every game server: it holds game data, not player data.
        /// <see cref="InitialCatalog"/> is not what the game server connects to — it is overridden by this
        /// and <see cref="TelecasterCatalog"/>.
        /// </summary>
        public string ArcadiaCatalog { get; set; } = "Arcadia";

        /// <summary>
        /// The character database. One per game server: a second server (a x5 one next to a x1, say) sets its
        /// own in its <c>appsettings.{env}.json</c>, otherwise both would share the same characters.
        /// </summary>
        public string TelecasterCatalog { get; set; } = "Telecaster";
    }
}