using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Linq;
using System.Runtime.CompilerServices;

namespace Vanguard.VCS.Client.UI.ClientWindow.LoginPages;

public class UnitSelectionViewModel : INotifyPropertyChanged
{
    public UnitSelectionViewModel(List<KeyValuePair<string, string>> availableUnits, List<string> availableCoalitions, List<KeyValuePair<int, string>> availableRoles)
    {
        _originalUnitSuggestions = availableUnits;
        UnitSuggestions = new ObservableCollection<KeyValuePair<string, string>>(availableUnits);
        Coalitions = availableCoalitions;
        Roles = availableRoles.Select(role => role.Value).ToList();
    }
    
    public event PropertyChangedEventHandler? PropertyChanged;

    /// <summary>
    /// Sets property if it does not equal existing value. Notifies listeners if change occurs.
    /// </summary>
    /// <typeparam name="T">Type of property.</typeparam>
    /// <param name="member">The property's backing field.</param>
    /// <param name="value">The new value.</param>
    /// <param name="propertyName">Name of the property used to notify listeners.  This
    /// value is optional and can be provided automatically when invoked from compilers
    /// that support <see cref="CallerMemberNameAttribute"/>.</param>
    protected virtual bool SetProperty<T>(ref T member, T value, [CallerMemberName] string? propertyName = null)
    {
        if (EqualityComparer<T>.Default.Equals(member, value))
        {
            return false;
        }

        member = value;
        OnPropertyChanged(propertyName);
        return true;
    }

    /// <summary>
    /// Notifies listeners that a property value has changed.
    /// </summary>
    /// <param name="propertyName">Name of the property, used to notify listeners.</param>
    protected void OnPropertyChanged([CallerMemberName] string? propertyName = null)
        => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    
    private ObservableCollection<KeyValuePair<string, string>>? _unitSuggestions;
    private readonly List<KeyValuePair<string, string>>? _originalUnitSuggestions;
    public ObservableCollection<KeyValuePair<string, string>>? UnitSuggestions
    {
        get => _unitSuggestions;
        set => SetProperty(ref _unitSuggestions, value);
    }
    
    private string? _autoSuggestBox2Text;
    public string? UnitText
    {
        get => _autoSuggestBox2Text;
        set
        {
            if (!SetProperty(ref _autoSuggestBox2Text, value) ||
                _originalUnitSuggestions == null || value == null) return;
            var searchResult = _originalUnitSuggestions.Where(x => IsMatch(x.Value, value) || IsMatch(x.Key, value)).ToList();
            UnitSuggestions = new ObservableCollection<KeyValuePair<string, string>>(searchResult);
        }
    }
    
    private static bool IsMatch(string item, string currentText)
    {
        return item.Contains(currentText, StringComparison.OrdinalIgnoreCase);
    }
    
    public IList<string> Coalitions { get; }
    public IList<string> Roles { get;  }
    
    public string SelectedCoalition { get; set; } = string.Empty;
    public string SelectedRole { get; set; } = string.Empty;
}