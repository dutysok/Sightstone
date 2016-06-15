﻿using System;
using Caliburn.Micro;
using Sightstone.Views;

namespace Sightstone.ViewModels
{
    public class LoginViewModel : Screen, IShell
    {
        bool _loginGridIsVisible;
        public bool LoginGridIsVisible {
            get
            {
                return _loginGridIsVisible;
            }
            set
            {
                if (_loginGridIsVisible != value)
                {
                    _loginGridIsVisible = value;
                    NotifyOfPropertyChange(() => LoginGridIsVisible);
                }
            }

        }

        public void Unlock(EventArgs args)
        {
            
        }

        public void AddAccount(EventArgs args)
        {
            LoginGridIsVisible = true;
        }
    }
}
