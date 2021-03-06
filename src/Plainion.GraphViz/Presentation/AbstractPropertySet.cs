﻿using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace Plainion.GraphViz.Presentation
{
    public abstract class AbstractPropertySet : INotifyPropertyChanged
    {
        protected AbstractPropertySet(string ownerId)
        {
            OwnerId = ownerId;
        }

        public string OwnerId { get; private set; }

        public event PropertyChangedEventHandler PropertyChanged;

        protected void SetProperty<T>(ref T member, T value, [CallerMemberName] string propertyName = null)
        {
            if (object.ReferenceEquals(member, default(T)) && object.ReferenceEquals(value, default(T)))
            {
                return;
            }

            if (!object.ReferenceEquals(member, default(T)) && member.Equals(value))
            {
                return;
            }

            member = value;

            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }
    }
}
