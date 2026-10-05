using Microsoft.AspNetCore.Identity;

namespace LabScaner.Infrastructure.Identity;

/// <summary>Сообщения Identity на русском — их видит администратор при создании пользователей и смене пароля.</summary>
internal sealed class RussianIdentityErrorDescriber : IdentityErrorDescriber
{
    public override IdentityError DuplicateUserName(string userName) =>
        new() { Code = nameof(DuplicateUserName), Description = $"Логин «{userName}» уже занят." };

    public override IdentityError InvalidUserName(string? userName) =>
        new() { Code = nameof(InvalidUserName), Description = $"Недопустимый логин «{userName}»: только латинские буквы, цифры и символы -._@+." };

    public override IdentityError PasswordTooShort(int length) =>
        new() { Code = nameof(PasswordTooShort), Description = $"Пароль должен быть не короче {length} символов." };

    public override IdentityError PasswordMismatch() =>
        new() { Code = nameof(PasswordMismatch), Description = "Неверный пароль." };

    public override IdentityError DuplicateRoleName(string role) =>
        new() { Code = nameof(DuplicateRoleName), Description = $"Роль «{role}» уже существует." };

    public override IdentityError UserAlreadyInRole(string role) =>
        new() { Code = nameof(UserAlreadyInRole), Description = $"Пользователь уже в роли «{role}»." };

    public override IdentityError UserLockoutNotEnabled() =>
        new() { Code = nameof(UserLockoutNotEnabled), Description = "Для пользователя не включена блокировка." };
}
