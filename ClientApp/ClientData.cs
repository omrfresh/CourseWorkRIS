[Serializable]
public class ClientData
{
    public string Mode { get; set; } // Режим работы: однопоточный или многопоточный
    public List<string> FilePaths { get; set; } // Список изображений
    public string ReferenceImage { get; set; } // Путь к изображению
    public int ThreadCount { get; set; } // Количество потоков
}
