﻿using Microsoft.AspNetCore.Mvc;
using System.Text;
using System.Security.Cryptography;
using Sociala.Data;
using Sociala.Models;
using Sociala.ViewModel;
using EncryptServices;
using EmailSendertServices;
using System;
using AuthorizationService;
using Microsoft.EntityFrameworkCore.Storage;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using System.Security.Claims;
using Sociala.Migrations;
using System.Linq;
using Sociala.Services;

namespace Sociala.Controllers
{
    public class UserController : Controller
    {
        private readonly AppData _context;

        private readonly AppData appData;
        private readonly IEmailSender emailSender;
        private readonly IEncrypt encryptclass;
        private readonly IAuthorization authorization;
        private readonly IConfiguration configuration;
        private readonly ICheckRelationShip CheckRelationShip;
        private readonly INotification notificationSerivce;

        public UserController(AppData appData, IEncrypt encryptClass, IEmailSender emailSender,
                                IAuthorization authorization, IConfiguration configuration,
                                ICheckRelationShip CheckRelationShip, INotification notificationSerivce)
        {
            this.appData = appData;
            this.encryptclass = encryptClass;
            this.emailSender = emailSender;
            this.authorization = authorization;
            this.configuration = configuration;
            this.CheckRelationShip = CheckRelationShip;
            this.notificationSerivce = notificationSerivce;
        }
        public IActionResult Profile(string Id)
        {
            try
            {
                if (!authorization.IsLoggedIn())
                    return RedirectToAction("LogIn", "User");
                if (Id == null)
                    Id = authorization.GetId();
                if (authorization.IsAdmin(Id))
                   return RedirectToAction("Index", "home");
                if (authorization.IsBanned(Id))
                    return View("ErrorPage");
                if (CheckRelationShip.IsBlock(Id))
                    return View("ErrorPage");
            }
            catch 
            {
                return View("ErrorPage");
            }

            var user = appData.User.FirstOrDefault(u => u.Id == Id);

            var posts = appData.Post.Where(post => post.UserId == Id)
                                      .Join(appData.User,
                                            post => post.UserId,
                                            user => user.Id,
                                            (post, friend) =>
                                            new PostInfo
                                             ()
                                            {
                                                Id = post.Id,
                                                PostContent = post.content,
                                                PostImj = post.Imj,
                                                UserPhoto = friend.UrlPhoto,
                                                UserName = friend.UesrName,
                                                UserId = friend.Id,
                                                CreateAt = post.CreateAt,
                                                IsHidden = post.IsHidden,

                                                Isliked = ((!(appData.Like.Contains(new Like
                                                {
                                                    PostId = post.Id,
                                                    UserId = authorization.GetId()
                                                }))) ? false
                                                  : true),

                                            })
                                           .Where(p => !p.IsHidden).OrderByDescending(p => p.CreateAt)
                                        .ToList();



            var sharedPosts = appData.SharePost.Include(p => p.Post).Include(p => p.Post.User).Where(post => post.UserId == Id).Join(appData.User,
                post => post.UserId,
                friend => friend.Id,
                (post, friend) => new PostInfo
                {
                    Id = post.Id,
                    OriginalId = post.Post.Id,
                    PostContent = post.Content,
                    OriginalPostContent = post.Post.content,
                    PostImj = post.Post.Imj,
                    UserPhoto = post.User.UrlPhoto,
                    OriginalUserPhoto = post.Post.User.UrlPhoto,
                    UserName = post.User.UesrName,
                    OriginalUserName = post.Post.User.UesrName,
                    UserId = friend.Id,
                    OriginalUserId = post.Post.UserId,
                    CreateAt = post.CreatedAt,
                    IsHidden = (post.IsHidden | post.Post.IsHidden),
                    IsBanned = (friend.IsBanned | post.User.IsBanned),
                    Isliked = (appData.Like.Contains(new Like
                    {
                        PostId = post.Post.Id,
                        UserId = post.UserId
                    }))
                })
                .Where(p => !p.IsHidden && !p.IsBanned)
                .ToList();

            posts.AddRange(sharedPosts);

            ViewBag.posts = posts.OrderByDescending(p => p.CreateAt);

            Id = authorization.GetId();
            ViewBag.Id = Id;

            return View(user);
        }
        public IActionResult EditProfile()
        {
            if (!authorization.IsLoggedIn())
                return RedirectToAction("LogIn", "User");
        
            string userId = authorization.GetId();

            if (authorization.IsAdmin(userId))
                return RedirectToAction("Index", "home");
                
            var user = appData.User.FirstOrDefault(u => u.Id == userId);

            if (user == null)
            {
                return NotFound();
            }
            return View(user);
        }

        [HttpPost]
        public IActionResult EditProfile(User updatedUser, IFormFile photo)
        {
            string userId = authorization.GetId();
            var user = appData.User.FirstOrDefault(u => u.Id == userId);

            if (user == null)
            {
                return NotFound();
            }

            user.UesrName = updatedUser.UesrName;
            user.PhoneNumber = updatedUser.PhoneNumber;
            if (user.PhoneNumber.Length != 11 || !(user.PhoneNumber.StartsWith("010") ||
                                             user.PhoneNumber.StartsWith("011") ||
                                             user.PhoneNumber.StartsWith("012") ||
                                             user.PhoneNumber.StartsWith("015")))
            {
                ViewBag.PhoneNumberMessage = "Enter valid egyptian phonenumber";
                return View(user);
            }
            user.bio = updatedUser.bio;
            var file = HttpContext.Request.Form.Files;

            if (file.Count() > 0)
            {
                if (!Path.GetExtension(file[0].FileName).Equals(".jpg") && !Path.GetExtension(file[0].FileName).Equals(".png") && !Path.GetExtension(file[0].FileName).Equals(".jpeg"))
                {
                    ViewBag.PhotoMessage = "Upload photo with Extension JPG,PNG or JPEG";
                    return View(user);
                }
                string imageName = Guid.NewGuid().ToString() + Path.GetExtension(file[0].FileName);
                var filePath = Path.Combine("wwwroot", "imj", imageName);

                using (var fileStream = new FileStream(filePath, FileMode.Create))
                {
                    file[0].CopyTo(fileStream);
                }

                user.UrlPhoto = $"/imj/{imageName}";
            }
            else
                user.UrlPhoto = user.UrlPhoto;

            appData.SaveChanges();

            return Redirect($"/user/profile/{user.Id}");
        }


        public async Task<IActionResult> AddFriend(string Id, string Place)
        {
            if (!authorization.IsLoggedIn())
            {
                return RedirectToAction("LogIn", "User");
            }
            string userId = authorization.GetId();
            if (authorization.IsAdmin(userId))
                return RedirectToAction("index", "Home");
            if (CheckRelationShip.IsBlock(Id))
                return View("ErrorPage");
            try
            {
                var requestingUserId = authorization.GetId();
                if (appData.Request.Where(r => (r.RequestingUserId.Equals(requestingUserId) && r.RequestedUserId.Equals(Id))|| (r.RequestedUserId.Equals(requestingUserId) && r.RequestingUserId.Equals(Id))).Count()>0)
                    return View("ErrorPage");
                var request = new Request();
                request.RequestingUserId = requestingUserId;
                request.RequestedUserId = Id;
                appData.Request.Add(request);
                appData.SaveChanges();
                await notificationSerivce.SendFriendRequestNotification(request.RequestingUserId, request.RequestedUserId);

            }
            catch
            {
                return View("ErrorPage");
            }
            if (Place == "Search") { 

                return Redirect($"/Home/Search/?name={TempData["Name"]}");
                
            }
            if (Place == "Friends")
            {

                return Redirect($"/User/Friends/{TempData["IdToSearch"]}");

            }
        
            else return Redirect($"/user/Profile/{Id}");

        }
        public IActionResult DeleteRequest(string Id ,string Place)
        {
            if (!authorization.IsLoggedIn())
            {
                return RedirectToAction("LogIn", "User");
            }
            string userId = authorization.GetId();
            if (authorization.IsAdmin(userId))
                return RedirectToAction("index", "Home");

            try
            {
                var DeleteResult = appData.Request.Where(p => p.RequestedUserId == authorization.GetId() && p.RequestingUserId == Id);
                appData.Request.Remove(DeleteResult.First());
                appData.SaveChanges();
            }
            catch
            {
                return View("ErrorPage");
            }
            if (Place == "Index")
                return Redirect("/Home/Index");
            if (Place == "Search")
            {

                return Redirect($"/Home/Search/?name={TempData["Name"]}");

            }
            if (Place == "Friends")
            {

                return Redirect($"/User/Friends/{TempData["IdToSearch"]}");

            }
            if (Place == "Profile")
            {

                return Redirect($"/user/Profile/{Id}");

            }
            else return RedirectToAction("ShowRequest");

        }
        public async Task <IActionResult> ConfirmRequest(string Id, string Place)
        {
            if (!authorization.IsLoggedIn())
            {
                return RedirectToAction("LogIn", "User");
            }
            string userId = authorization.GetId();
            if (authorization.IsAdmin(userId))
                return RedirectToAction("index", "Home");
            try
            {
                Friend friend = new Friend();
                friend.RequestedUserId = authorization.GetId();
                friend.RequestingUserId = Id;
                appData.Friend.Add(friend);
                var DeleteResult = appData.Request.Where(p => p.RequestedUserId == authorization.GetId() && p.RequestingUserId == Id);
                appData.Request.Remove(DeleteResult.First());
                appData.SaveChanges();
                await notificationSerivce.SendFriendRequestAcceptNotification(friend.RequestedUserId, friend.RequestingUserId);
            }
            catch
            {
                return View("ErrorPage");
            }
            if (Place == "Index")
                return Redirect("/Home/Index");
            if (Place == "Search")
            {

                return Redirect($"/Home/Search/?name={TempData["Name"]}");

            }
            if (Place == "Friends")
            {

                return Redirect($"/User/Friends/{TempData["IdToSearch"]}");

            }
            if (Place == "Profile")
            {

                return Redirect($"/user/Profile/{Id}");

            }
            else return RedirectToAction("ShowRequest");
        }
        public IActionResult Derequest(string Id ,string place)
        {
            if (!authorization.IsLoggedIn())
            {
                return RedirectToAction("LogIn", "User");
            }
            string userId = authorization.GetId();
            if (authorization.IsAdmin(userId))
                return RedirectToAction("index", "Home");
            try
            {
                var DeleteResult = appData.Request.Where(p => p.RequestedUserId == Id && p.RequestingUserId == authorization.GetId());
                appData.Request.Remove(DeleteResult.First());
                appData.SaveChanges();
                if (place=="Profile")
                    return Redirect($"/User/Profile/{Id}");
            }
            catch
            {
                return View("ErrorPage");
            }
            if (place == "Search")
            {

                return Redirect($"/Home/Search/?name={TempData["Name"]}");

            }
            if (place == "Friends")
            {

                return Redirect($"/User/Friends/{TempData["IdToSearch"]}");

            }
            return Redirect("/Home/Search");

        }

        public IActionResult ShowRequest()
        {
            string id = authorization.GetId();
            var RequestsId = appData.Request.Where(r => r.RequestedUserId.Equals(id)).Select(r => r.RequestingUserId);
            ViewBag.Requests = appData.User.Where(u => RequestsId.Contains(u.Id)&& !u.IsBanned);

            return View();
        }
     
        public IActionResult Friends( string Id)
        {
            if (!authorization.IsLoggedIn())
                return RedirectToAction("LogIn", "User");
            
            string id= authorization.GetId();
            ViewBag.IsAdmin= authorization.IsAdmin(id);
            var Result = appData.Friend.Where(p => p.RequestedUserId == Id || p.RequestingUserId == Id).Select(p=>Id.Equals(p.RequestedUserId)? p.RequestingUserId :p.RequestedUserId).ToList();
            var FinalResult=appData.User.Where(u => Result.Contains(u.Id)&&!u.IsBanned).ToList();
            ViewBag.Friends = FinalResult;
            TempData["IdToSearch"] = Id;////// this Id pass to search to make query easy
            
            return View();

        }
        public IActionResult UnFriend(string Id)
        {

            try
            {

                var Result = appData.Friend.Where(p => (p.RequestedUserId == authorization.GetId() && p.RequestingUserId == Id) || (p.RequestedUserId == Id && p.RequestingUserId == authorization.GetId()));
                appData.Friend.Remove(Result.First());
                TempData["IdToProfile"] = Id;
                appData.SaveChanges();
                return Redirect($"/User/Profile/?Id={TempData["IdToProfile"]}");
            }
            catch
            {

                return View("ErrorPage");
            }
        }

        public IActionResult Block(string Id)
        {
            if (!authorization.IsLoggedIn())
                return RedirectToAction("LogIn", "User");
            if (authorization.IsAdmin(authorization.GetId()))
                return RedirectToAction("index", "Home");



            if (!CheckRelationShip.IsBlock(Id)) {
                Block block = new Block();
                block.Blocking = authorization.GetId();
                block.Blocked = Id;
                appData.Block.Add(block);
                appData.SaveChanges();

                var ResultOfFriend = appData.Friend.Where(p => (p.RequestedUserId == authorization.GetId() && p.RequestingUserId == Id) || (p.RequestedUserId == Id && p.RequestingUserId == authorization.GetId()));
                if (ResultOfFriend.Count() >= 1)
                {
                    appData.Friend.Remove(ResultOfFriend.First());
                    appData.SaveChanges();
                }
                else
                {
                    var ResultOfRequest = appData.Request.Where(p => (p.RequestedUserId == authorization.GetId() && p.RequestingUserId == Id) || (p.RequestedUserId == Id && p.RequestingUserId == authorization.GetId()));

                    if (ResultOfRequest.Count() >= 1)
                    {
                        appData.Request.Remove(ResultOfRequest.First());
                        appData.SaveChanges();
                    }
                }

                return Redirect("/Home/Index");
            }
            else 
            {
                return View("ErrorPage");

            }

            
        }
        public IActionResult Blocks()
        {

            var Result = appData.Block.Where(p => p.Blocking == authorization.GetId()).Select(p=>p.Blocked);
            var FinalResult = appData.User.Where(u => Result.Contains(u.Id) && !u.IsBanned).ToList();
            ViewBag.Blocks= FinalResult;
            return View();

        }
        public IActionResult UnBlock(string Id)
        {

            var Result = appData.Block.Where(p => p.Blocking == authorization.GetId() && p.Blocked == Id);
            appData.Block.Remove(Result.First());
            appData.SaveChanges();
            return RedirectToAction("Blocks");

        }




        [HttpPost]
        public IActionResult Search( string Name )
        {
            string  Id = Convert.ToString( TempData["IdToSearch"]);////come from frinds
            var Result = appData.Friend.Where(p => p.RequestedUserId == Id || p.RequestingUserId == Id).Select(p => Id.Equals(p.RequestedUserId) ? p.RequestingUserId : p.RequestedUserId).ToList();
            var ResultOfSearch = appData.User.Where(u => Result.Contains(u.Id) && u.UesrName.Contains(Name)).ToList();
            ViewBag.Search = ResultOfSearch;
            return View();
        }
        public IActionResult Photos(string Id)
        {
            if (!authorization.IsLoggedIn())
                return RedirectToAction("LogIn", "User");

            if (authorization.IsAdmin(Id))
                return RedirectToAction("Index", "home");

            var ResultOfPosts = appData.Post.Where(p => p.UserId==Id&&!p.IsHidden).ToList();
            ViewBag.Photos = ResultOfPosts;
            return View();
        }


        private bool IsPasswordValid(string password)
        {
            if (password.Length < 8)
            {
                return false;
            }

            if (!password.Any(char.IsDigit))
            {
                return false;
            }

            if (!password.Any(char.IsUpper))
            {
                return false;
            }

            if (!password.Any(char.IsLower))
            {
                return false;
            }


            return true;
        }
        /**************************************************************************************/

        private string Hash(string input)
        {
            using (SHA256 sha256Hash = SHA256.Create())
            {
                byte[] bytes = sha256Hash.ComputeHash(Encoding.UTF8.GetBytes(input));

                StringBuilder builder = new StringBuilder();
                for (int i = 0; i < bytes.Length; i++)
                {
                    builder.Append(bytes[i].ToString("x2"));
                }
                return builder.ToString();
            }
        }
        /**************************************************************************************/


        public IActionResult Login()
        {
            if (authorization.IsLoggedIn())
                return RedirectToAction("index", "Home");
            return View();
        }


        [HttpPost]
        public IActionResult Login(LoginInfo loginInfo)
        {
            var user = appData.User.SingleOrDefault(u => u.Email.Equals(loginInfo.Email));
            if (user == null)
            {
                ViewBag.NonFoundEmailMessage = "Wrong Email";
                return View();
            }
            if (!Hash(loginInfo.Password).Equals(user.Password))
            {
                ViewBag.WrongPassword = "Wrong password";
                return View();
            }
            if (user.IsBanned)
            {
                return View("BlockPage");
            }
            if (!user.IsActive)
            {
                Response.Cookies.Append("ActiveKey", encryptclass.Encrypt(user.ActiveKey, configuration.GetSection("Key").ToString()));
                Response.Cookies.Append("TempId", encryptclass.Encrypt(user.Id, configuration.GetSection("Key").ToString()));
                return RedirectToAction("ConfirmEmail", "User");
            }
            if (loginInfo.RememberMe)
            {
                CookieOptions cookie = new CookieOptions();
                cookie.Expires = DateTime.Now.AddDays(30);
                Response.Cookies.Append("id", encryptclass.Encrypt(user.Id, configuration.GetSection("Key").ToString()), cookie);
            }
            else
            {
                Response.Cookies.Append("id", encryptclass.Encrypt(user.Id, configuration.GetSection("Key").ToString()));
            }
            return RedirectToAction("index", "Home");
        }

        /**************************************************************************************/

        public IActionResult LogOut()
        {
            CookieOptions cookie = new CookieOptions();
            cookie.Expires = DateTime.Now.AddDays(-1);
            Response.Cookies.Append("id", "1", cookie);
            return RedirectToAction("index", "Home");
        }
        /**************************************************************************************/

        public IActionResult SignUp()
        {
            if (Request.Cookies["id"] != null)
                return RedirectToAction("index", "Home");
            return View();
        }


        [HttpPost]
        public async Task<IActionResult> SignUp(User user)
        {
            bool emailExists = appData.User.Any(u => u.Email == user.Email);

            if (emailExists)
            {
                ViewBag.EmailMessage = "Email already exists. Please use a different email.";
                return View();
            }
            if (user.PhoneNumber.Length != 11 || !(user.PhoneNumber.StartsWith("010") ||
                                             user.PhoneNumber.StartsWith("011") ||
                                             user.PhoneNumber.StartsWith("012") ||
                                             user.PhoneNumber.StartsWith("015")))
            {
                ViewBag.PhoneNumberMessage = "Enter valid egyptian phonenumber";
                return View();
            }
            if (!IsPasswordValid(user.Password))
            {
                ViewBag.PasswordMessage = "Password must be 8 characters long , at least contain one uppercase , at least one lowercase character and at least one symbol.";
                return View();
            }
            var file = HttpContext.Request.Form.Files;

            if (file.Count() > 0)
            {
                if (!Path.GetExtension(file[0].FileName).Equals(".jpg") && !Path.GetExtension(file[0].FileName).Equals(".png") && !Path.GetExtension(file[0].FileName).Equals(".jpeg"))
                {
                    ViewBag.PhotoMessage = "Upload photo with Extension JPG,PNG or JPEG";
                    return View();
                }
                string imageName = Guid.NewGuid().ToString() + Path.GetExtension(file[0].FileName);
                var filePath = Path.Combine("wwwroot", "imj", imageName);

                using (var fileStream = new FileStream(filePath, FileMode.Create))
                {
                    file[0].CopyTo(fileStream); // Save in the Images folder
                }

                user.UrlPhoto = $"/imj/{imageName}";
            }
            else
                user.UrlPhoto = $"/imj/default.jpg";
            user.Id = Guid.NewGuid().ToString();
            user.Password = Hash(user.Password);
            user.IsActive = false;
            user.CreateAt= DateTime.Now;
            user.RoleId = 1;
            Random random = new Random();
            for (int i = 0; i < 6; i++)
            {
                user.ActiveKey += random.Next(0, 10); 
            }
            try
            {
                await emailSender.SendEmailAsync(user.Email, "Confirm email", @"
                <html>
                <head>
                    <style>
                        body {
                            font-family: Arial, sans-serif;
                            line-height: 1.6;
                            margin: 0;
                            padding: 0;
                        }

                        .container {
                            max-width: 600px;
                            margin: 0 auto;
                            padding: 20px;
                            border: 1px solid #ddd;
                            border-radius: 5px;
                        }

                        .header {
                            background-color: #f5f5f5;
                            padding: 10px 0;
                            text-align: center;
                            border-bottom: 1px solid #ddd;
                        }

                        .body-content {
                            padding: 20px 0;
                        }

                        .verification-code {
                            font-size: 18px;
                            font-weight: bold;
                            color: #007bff;
                        }
                    </style>
                </head>
                <body>
                    <div class='container'>
                        <div class='header'>
                            <h2>Welcome to Sociala</h2>
                        </div>
                        <div class='body-content'>
                            <p>Dear " + user.UesrName + @",</p>
                            <p>Thank you for choosing Sociala, your gateway to a vibrant online community.</p>
                            <p>To verify your account, please use the following verification code:</p>
                            <p class='verification-code'>" + user.ActiveKey + @"</p>
                            <p>Thank you.</p>
                        </div>
                    </div>
                </body>
                </html>
                ");

                appData.Add(user);
                await appData.SaveChangesAsync();
                Response.Cookies.Append("ActiveKey", encryptclass.Encrypt(user.ActiveKey, configuration.GetSection("Key").ToString()));
                Response.Cookies.Append("TempId", encryptclass.Encrypt(user.Id, configuration.GetSection("Key").ToString()));
            }
            catch
            {
                return View("ErrorPage");
            }
            return RedirectToAction("ConfirmEmail", "User");
        }
        /**************************************************************************************/

        public IActionResult ConfirmEmail()
        {
            if (Request.Cookies["TempId"] == null || Request.Cookies["id"] != null)
                return RedirectToAction("index", "Home");
            return View();
        }


        [HttpPost]
        public IActionResult ConfirmEmail(string key)
        {

            if (key != null && key.Equals(encryptclass.Decrypt(Request.Cookies["ActiveKey"], configuration.GetSection("Key").ToString())))
            {
                CookieOptions cookie = new CookieOptions();
                var user = appData.User.SingleOrDefault(u => u.Id.Equals(encryptclass.Decrypt(Request.Cookies["TempId"], configuration.GetSection("Key").ToString())));
                user.IsActive = true;
                appData.SaveChanges();
                cookie.Expires = DateTime.Now.AddDays(-1);
                Response.Cookies.Append("TempId", "1", cookie);
                Response.Cookies.Append("ActiveKey", "1", cookie);
                Response.Cookies.Append("id", encryptclass.Encrypt(user.Id, configuration.GetSection("Key").ToString()));
            }
            else
            {
                ViewBag.ConfirmEamil = "Wrong key";
                return View();
            }
            return RedirectToAction("index", "Home");
        }

        public IActionResult EditPassword()
        {
            if (!authorization.IsLoggedIn())
                return RedirectToAction("LogIn", "User");
            string userId = authorization.GetId();

            if (authorization.IsAdmin(userId))
                return RedirectToAction("Index", "home");
            return View();
        }

        [HttpPost]
        public IActionResult EditPassword(EditPassword model)
        {
            var userId = authorization.GetId();
            var user = appData.User.FirstOrDefault(u => u.Id == userId);

            if (user == null)
            {
                return View("ErrorPage"); ;
            }

            if (!Hash(model.CurrentPassword).Equals(user.Password))
            {
                ViewBag.CurrentPasswordMessage = "Incorrect current password.";
                return View();
            }

            if (!IsPasswordValid(model.NewPassword))
            {
                ViewBag.PasswordMessage = "Password must be 8 characters long, contain at least one uppercase letter, one lowercase letter, and one digit.";
                return View();
            }
           
            user.Password = Hash(model.NewPassword);
            appData.SaveChanges();

            return RedirectToAction("EditProfile", new { Id = userId });
        }


    }
}
