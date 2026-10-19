namespace PhotoApp.Core.Models;

/// <summary>Finesse d'une date : un nom WhatsApp ne donne que le jour, un EXIF la seconde, parfois mieux.</summary>
public enum DatePrecision
{
    Day = 0,
    Minute = 1,
    Second = 2,
    SubSecond = 3
}
