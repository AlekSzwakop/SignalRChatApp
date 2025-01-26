using System;
using Microsoft.AspNetCore.Identity;

namespace API.Models;

public class AppUsers:IdentityUser
{
    public string? FullName {get; set;}
    public string? ProfileImage {get; set;}
}
