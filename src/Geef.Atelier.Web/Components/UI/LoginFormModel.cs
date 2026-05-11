using System.ComponentModel.DataAnnotations;

namespace Geef.Atelier.Web.Components.UI;

public sealed class LoginFormModel
{
    [Required] public string? Username { get; set; }
    [Required] public string? Password { get; set; }
}
