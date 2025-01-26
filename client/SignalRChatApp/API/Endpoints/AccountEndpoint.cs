using System;
using System.Runtime.InteropServices;
using System.Threading.Tasks;
using API.Common;
using API.DTOs;
using API.Extensions;
using API.Models;
using API.Services;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;


namespace API.Endpoints
{
    // 1. Klasa z metod� rozszerzaj�c� musi by� static
    public static class AccountEndpoint
    {
        public static RouteGroupBuilder MapAccountEndpoint(this WebApplication app)
        {

            var group = app.MapGroup("/api/account").WithTags("account");

            group.MapPost("/register",
                async (
                    HttpContext context,            // dodana nazwa parametru
                    UserManager<AppUsers> userManager,  // poprawna nazwa zmiennej (nie UserManager z wielkiej litery)
                    [FromForm] string fullName,
                    [FromForm] string email,
                    [FromForm] string password,
                    [FromForm] string userName,
                    IFormFile? profileImage
                ) =>
                {

                    var userFromDb = await userManager.FindByEmailAsync(email);


                    if(profileImage is null)
                    {
                        return Results.BadRequest(Response<string>.Failure("Wymagane jest zdjecie"));
                    }

                    var picture = await FileUpload.Upload(profileImage);

                    picture = $"{context.Request.Scheme}://{context.Request.Host}/uploads/{picture}";
                    if (userFromDb is not null)
                    {
                        return Results.BadRequest(Response<string>.Failure("Uzytkownik istnieje."));
                    }

                    var user = new AppUsers
                    {
                        Email = email,
                        FullName = fullName,
                        UserName = userName,
                        ProfileImage = picture,
           
                    };
           
                    var result = await userManager.CreateAsync(user, password);

                    if (!result.Succeeded)
                    {
                        var errorMsg = result.Errors.Select(x => x.Description).FirstOrDefault() ?? "Blad";
                        return Results.BadRequest(Response<string>.Failure(errorMsg));
                    }

                    return Results.Ok(Response<string>.Success("", "Uzytkownik stworzony poprawnie"));
                }).DisableAntiforgery();

            group.MapPost("/login",async (UserManager<AppUsers> userManager,TokenService tokenService, LoginDto dto) =>
            {
                if(dto is null)
                {
                    return Results.BadRequest(Response<string>.Failure("zly login"));
                }

                var user = await userManager.FindByEmailAsync(dto.Email);

                if(user is null)
                {
                    return Results.BadRequest(Response<string>.Failure("nie znaleziono uzytkownika"));
                }

                var result = await userManager.CheckPasswordAsync(user!, dto.Password);

                if (!result)
                {
                    return Results.BadRequest(Response<string>.Failure("zle haslo"));
                }

                var token = tokenService.GenerateToken(user.Id, user.UserName!);

                return Results.Ok(Response<string>.Success(token, "Login udany"));


            });   
            group.MapGet("/me", async (HttpContext context, UserManager<AppUsers> userManager) =>
            {
                var currentLoggedInUserId = context.User.GetUserId()!;

                var currentLoggedInUser = await userManager.Users.SingleOrDefaultAsync(x => x.Id == currentLoggedInUserId.ToString());

                return Results.Ok(Response<AppUsers>.Success(currentLoggedInUser!, "User fetched successfully."));
            }).RequireAuthorization();
            return group;
        }
    }
}
