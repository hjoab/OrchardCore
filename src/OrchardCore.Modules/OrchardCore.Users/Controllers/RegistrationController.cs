using System;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Localization;
using Microsoft.Extensions.Localization;
using Microsoft.Extensions.Logging;
using OrchardCore.DisplayManagement.Notify;
using OrchardCore.Email;
using OrchardCore.Entities;
using OrchardCore.Modules;
using OrchardCore.Settings;
using OrchardCore.Users.Models;
using OrchardCore.Users.Services;
using OrchardCore.Users.ViewModels;

namespace OrchardCore.Users.Controllers
{
    [Feature("OrchardCore.Users.Registration")]
    public class RegistrationController : Controller
    {
        private readonly DefaultControllerService _controllerService;
        private readonly UserManager<IUser> _userManager;
        private readonly IAuthorizationService _authorizationService;
        private readonly ISiteService _siteService;
        private readonly INotifier _notifier;
        private readonly IEmailAddressValidator _emailAddressValidator;
        private readonly ILogger _logger;
        private readonly IStringLocalizer S;
        private readonly IHtmlLocalizer H;

        public RegistrationController(
            DefaultControllerService controllerService,
            UserManager<IUser> userManager,
            IAuthorizationService authorizationService,
            ISiteService siteService,
            INotifier notifier,
            IEmailAddressValidator emailAddressValidator,
            ILogger<RegistrationController> logger,
            IHtmlLocalizer<RegistrationController> htmlLocalizer,
            IStringLocalizer<RegistrationController> stringLocalizer)
        {
            _controllerService = controllerService;
            _userManager = userManager;
            _authorizationService = authorizationService;
            _siteService = siteService;
            _notifier = notifier;
            _emailAddressValidator = emailAddressValidator ?? throw new ArgumentNullException(nameof(emailAddressValidator));
            _logger = logger;
            H = htmlLocalizer;
            S = stringLocalizer;
        }

        [HttpGet]
        [AllowAnonymous]
        public async Task<IActionResult> Register(string returnUrl = null)
        {
            var settings = (await _siteService.GetSiteSettingsAsync()).As<RegistrationSettings>();
            if (settings.UsersCanRegister != UserRegistrationType.AllowRegistration)
            {
                return NotFound();
            }

            ViewData["ReturnUrl"] = returnUrl;
            return View();
        }

        [HttpPost]
        [AllowAnonymous]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Register(RegisterViewModel model, string returnUrl = null)
        {
            var settings = (await _siteService.GetSiteSettingsAsync()).As<RegistrationSettings>();

            if (settings.UsersCanRegister != UserRegistrationType.AllowRegistration)
            {
                return NotFound();
            }

            if (string.IsNullOrEmpty(model.Email))
            {
                ModelState.AddModelError("Email", S["Email is required."]);
            }

            if (_emailAddressValidator.Validate(model.Email))
            {
                // Check if user with same email already exists
                var userWithEmail = await _userManager.FindByEmailAsync(model.Email);

                if (userWithEmail != null)
                {
                    ModelState.AddModelError("Email", S["A user with the same email already exists."]);
                }
            }
            else
            {
                ModelState.AddModelError("Email", S["Invalid email."]);
            }

            ViewData["ReturnUrl"] = returnUrl;

            if (TryValidateModel(model) && ModelState.IsValid)
            {
                var iUser = await _controllerService.RegisterUser(this, model, S["Confirm your account"]);
                // If we get a user, redirect to returnUrl
                if (iUser is User user)
                {
                    if (settings.UsersMustValidateEmail && !user.EmailConfirmed)
                    {
                        return RedirectToAction("ConfirmEmailSent", new { ReturnUrl = returnUrl });
                    }
                    if (settings.UsersAreModerated && !user.IsEnabled)
                    {
                        return RedirectToAction("RegistrationPending", new { ReturnUrl = returnUrl });
                    }

                    return RedirectToLocal(returnUrl.ToUriComponents());
                }
            }

            // If we got this far, something failed, redisplay form
            return View(model);
        }

        [HttpGet]
        [AllowAnonymous]
        public async Task<IActionResult> ConfirmEmail(string userId, string code)
        {
            if (userId == null || code == null)
            {
                return RedirectToAction(nameof(RegistrationController.Register), "Registration");
            }

            var user = await _userManager.FindByIdAsync(userId);

            if (user == null)
            {
                return NotFound();
            }

            var result = await _userManager.ConfirmEmailAsync(user, code);

            if (result.Succeeded)
            {
                return View();
            }

            return NotFound();
        }

        [HttpGet]
        [AllowAnonymous]
        public IActionResult ConfirmEmailSent(string returnUrl = null)
            => View(new { ReturnUrl = returnUrl });

        [HttpGet]
        [AllowAnonymous]
        public IActionResult RegistrationPending(string returnUrl = null)
            => View(new { ReturnUrl = returnUrl });

        [Authorize]
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> SendVerificationEmail(string id)
        {
            if (!await _authorizationService.AuthorizeAsync(User, Permissions.ManageUsers))
            {
                return Forbid();
            }

            var user = await _userManager.FindByIdAsync(id) as User;
            if (user != null)
            {
                await _controllerService.SendEmailConfirmationTokenAsync(this, user, S["Confirm your account"]);

                await _notifier.SuccessAsync(H["Verification email sent."]);
            }

            return RedirectToAction(nameof(AdminController.Index), "Admin");
        }

        private IActionResult RedirectToLocal(string returnUrl)
        {
            if (Url.IsLocalUrl(returnUrl))
            {
                return Redirect(returnUrl);
            }
            else
            {
                return Redirect("~/");
            }
        }
    }
}
