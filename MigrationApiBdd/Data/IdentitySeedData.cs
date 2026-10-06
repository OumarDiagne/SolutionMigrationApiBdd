using Microsoft.AspNetCore.Identity;
using MigrationApiBdd.Models.Identity;

namespace MigrationApiBdd.Data
{
    public static class IdentitySeedData
    {
        public static async Task SeedRolesAsync(IServiceProvider services)
        {
            var roleManager = services.GetRequiredService<RoleManager<IdentityRole>>();

            string[] roles = ["User", "Admin"];

            foreach (var roleName in roles)
            {
                if (!await roleManager.RoleExistsAsync(roleName))
                {
                    var result = await roleManager.CreateAsync(new IdentityRole(roleName));

                    if (!result.Succeeded)
                    {
                        throw new InvalidOperationException(
                            $"Impossible de créer le rôle '{roleName}'.");
                    }
                }
            }
        }

        public static async Task SeedAdminAsync(
            IServiceProvider services,
            IConfiguration configuration)
        {
            var userManager = services.GetRequiredService<UserManager<ApplicationUser>>();

            var email = configuration["SeedAdmin:Email"];
            var password = configuration["SeedAdmin:Password"];

            if (string.IsNullOrWhiteSpace(email) || string.IsNullOrWhiteSpace(password))
            {
                throw new InvalidOperationException(
                    "Les secrets SeedAdmin:Email et SeedAdmin:Password sont obligatoires.");
            }

            var adminUser = await userManager.FindByEmailAsync(email);

            if (adminUser is null)
            {
                adminUser = new ApplicationUser
                {
                    UserName = email,
                    Email = email,
                    EmailConfirmed = true
                };

                var createResult = await userManager.CreateAsync(adminUser, password);

                if (!createResult.Succeeded)
                {
                    var errors = string.Join(
                        "; ",
                        createResult.Errors.Select(error => error.Description));

                    throw new InvalidOperationException(
                        $"Impossible de créer le compte administrateur : {errors}");
                }
            }

            if (!await userManager.IsInRoleAsync(adminUser, "Admin"))
            {
                var roleResult = await userManager.AddToRoleAsync(adminUser, "Admin");

                if (!roleResult.Succeeded)
                {
                    var errors = string.Join(
                        "; ",
                        roleResult.Errors.Select(error => error.Description));

                    throw new InvalidOperationException(
                        $"Impossible d’attribuer le rôle Admin : {errors}");
                }
            }
        }
    }
}
