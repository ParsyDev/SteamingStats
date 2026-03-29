using UnityEngine;
using UnityEngine.Networking;
using UnityEngine.UI;
using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using SimpleJSON;
using TMPro;

public class SteamSalesDiagram : MonoBehaviour
{
    public enum Currency
    {
        USD,
        EUR
    }

    [Header("Steam API")]
    public string partnerApiKey = "YOUR_FINANCE_KEY";
    private string lastHighwatermark = "0";

    [Header("Diagram Settings")]
    public float margin = 50f;
    public float lineWidth = 3f;
    public bool paused = false;
    public Currency selectedCurrency = Currency.USD;

    [Header("UI Elements")]
    public Slider rangeSlider;
    public TMP_Text rangeText;
    public TMP_Text revenueSelectedText;
    public TMP_Text revenueTodayText;
    public TMP_Text revenueWeekText;
    public TMP_Text revenueMonthText;
    public TMP_Text revenueYearText;
    public TMP_Text revenueTotalText;
    public TMP_Text revenueCleanTotal;
    public TMP_Text additionalInfoText;
    public GameObject loadingScreen;
    public Toggle timezoneToggle;
    public TMP_Text timezoneLabel;
    public Toggle currencyToggle;
    public TMP_Text currencyLabel;
    
    [Header("New Revenue Window")]
    public GameObject newRevenueWindow;
    public TMP_Text newRevenueAmountText;
    public Button closeNewRevenueButton;

    public float paddingTop = 50f;
    public float paddingBottom = 50f;
    public float paddingLeft = 50f;
    public float paddingRight = 50f;
    private GUIStyle labelStyle;
    public Font myFont;

    private float maxValue = 0f;
    private const string RangePrefKey = "SalesDiagramRange";
    private const string TimezonePrefKey = "UseGermanTime";
    private const string CurrencyPrefKey = "UseCurrencyEUR";
    private const string LastTotalRevenuePrefKey = "LastTotalRevenue";

    // Changed: Use Dictionary to aggregate sales by date
    private Dictionary<DateTime, float> salesByDate = new Dictionary<DateTime, float>();
    private List<SaleEntry> salesEntries = new List<SaleEntry>();
    private int rangeCount = 0;

    private int datesToFetch = 0;
    private int datesFetched = 0;
    private bool dataReady = false;

    private bool useGermanTime = false;
    private int germanUtcOffsetHours = 1;
    private int usUtcOffsetHours = -5;
    private float usdToEurRate = 0.92f; // Default rate, will be updated from API

    private struct SaleEntry
    {
        public DateTime date;
        public float grossUSD;
        public SaleEntry(DateTime d, float g) { date = d; grossUSD = g; }
    }

    IEnumerator FetchExchangeRate()
    {
        string url = "https://api.exchangerate-api.com/v4/latest/USD";
        
        using (UnityWebRequest request = UnityWebRequest.Get(url))
        {
            yield return request.SendWebRequest();

            if (request.result == UnityWebRequest.Result.Success)
            {
                var json = JSONNode.Parse(request.downloadHandler.text);
                usdToEurRate = json["rates"]["EUR"].AsFloat;
                Debug.Log($"Exchange rate updated: 1 USD = {usdToEurRate} EUR");
            }
            else
            {
                Debug.LogWarning("Failed to fetch exchange rate, using default: " + usdToEurRate);
            }
        }
    }

    void OnApplicationPause(bool pauseStatus)
    {
        if (!pauseStatus)
        {
            Debug.Log("App resumed!");
            OnAppResume();
        }
    }

    void OnAppResume()
    {
        StartCoroutine(FetchChangedDates());
    }

    void Start()
    {   
        // FORCE REFETCH - UNCOMMENT TO CLEAR ALL DATA:
        // PlayerPrefs.DeleteAll();
        // lastHighwatermark = "0";
        // salesByDate.Clear();
        // salesEntries.Clear();
        
        if(!PlayerPrefs.HasKey("SteamPartnerApiKey"))loadingScreen.SetActive(false);
        partnerApiKey = PlayerPrefs.GetString("SteamPartnerApiKey", partnerApiKey);
        rangeCount = PlayerPrefs.GetInt(RangePrefKey, 0);
        useGermanTime = PlayerPrefs.GetInt(TimezonePrefKey, 0) == 1;
        selectedCurrency = PlayerPrefs.GetInt(CurrencyPrefKey, 0) == 1 ? Currency.EUR : Currency.USD;
        
        // FORCE FULL REFETCH - DELETE THIS AFTER ONE RUN
        lastHighwatermark = "0";
        salesByDate.Clear();
        salesEntries.Clear();
        maxValue = 0f;
        datesFetched = 0;
        dataReady = false;

        // Hide new revenue window initially
        if (newRevenueWindow)
        {
            newRevenueWindow.SetActive(false);
            // Set window to render above OnGUI
            Canvas canvas = newRevenueWindow.GetComponentInParent<Canvas>();
            if (canvas != null)
            {
                canvas.sortingOrder = 1000; // High value to ensure it's on top
            }
        }

        if (closeNewRevenueButton)
        {
            closeNewRevenueButton.onClick.AddListener(CloseNewRevenueWindow);
        }

        if (rangeSlider)
        {   
            rangeSlider.value = PlayerPrefs.GetInt("SliderKey", 1);
            rangeSlider.minValue = 1;
            rangeSlider.maxValue = 1;
            rangeSlider.onValueChanged.AddListener(OnSliderChanged);
        }

        if (timezoneToggle)
        {
            timezoneToggle.isOn = useGermanTime;
            timezoneToggle.onValueChanged.AddListener(OnTimezoneToggled);
            UpdateTimezoneLabel();
        }

        if (currencyToggle)
        {
            currencyToggle.isOn = (selectedCurrency == Currency.EUR);
            currencyToggle.onValueChanged.AddListener(OnCurrencyToggled);
            UpdateCurrencyLabel();
        }

        StartCoroutine(FetchExchangeRate());
        StartCoroutine(FetchChangedDates());
    }

    void OnTimezoneToggled(bool isOn)
    {
        useGermanTime = isOn;
        PlayerPrefs.SetInt(TimezonePrefKey, isOn ? 1 : 0);
        PlayerPrefs.Save();
        UpdateTimezoneLabel();
        UpdateRevenueTexts();
        UpdateDateRangeText();
    }

    void UpdateTimezoneLabel()
    {
        if (timezoneLabel)
        {
            timezoneLabel.text = useGermanTime ? "DE Time" : "US Time";
        }
    }

    void OnCurrencyToggled(bool isOn)
    {
        selectedCurrency = isOn ? Currency.EUR : Currency.USD;
        PlayerPrefs.SetInt(CurrencyPrefKey, isOn ? 1 : 0);
        PlayerPrefs.Save();
        UpdateCurrencyLabel();
        
        // Fetch latest exchange rate when switching to EUR
        if (isOn)
        {
            StartCoroutine(FetchExchangeRate());
        }
        
        UpdateRevenueTexts();
    }

    void UpdateCurrencyLabel()
    {
        if (currencyLabel)
        {
            currencyLabel.text = selectedCurrency == Currency.EUR ? "EUR" : "USD";
        }
    }

    DateTime GetLocalTime(DateTime utcTime)
    {
        int offsetHours = useGermanTime ? germanUtcOffsetHours : usUtcOffsetHours;
        return utcTime.AddHours(offsetHours);
    }

    float ConvertToDisplayCurrency(float usdAmount)
    {
        return selectedCurrency == Currency.EUR ? usdAmount * usdToEurRate : usdAmount;
    }

    IEnumerator FetchChangedDates()
    {
        string url = $"https://partner.steam-api.com/IPartnerFinancialsService/GetChangedDatesForPartner/v001/?key={partnerApiKey}&highwatermark={lastHighwatermark}";

        using (UnityWebRequest request = UnityWebRequest.Get(url))
        {
            yield return request.SendWebRequest();

            if (request.result == UnityWebRequest.Result.Success)
            {
                var json = JSONNode.Parse(request.downloadHandler.text);
                lastHighwatermark = json["response"]["result_highwatermark"];
                var datesArray = json["response"]["dates"].AsArray;

                datesToFetch = datesArray.Count;

                foreach (JSONNode dateNode in datesArray)
                {
                    StartCoroutine(FetchDetailedSales(dateNode));
                }
            }
            else
            {
                Debug.LogError("Error fetching changed dates: " + request.error);
            }
        }
    }

    IEnumerator FetchDetailedSales(string dateString)
    {
        ulong highwatermarkId = 0;
        bool hasMore = true;

        DateTime date = DateTime.Parse(dateString, null,
            System.Globalization.DateTimeStyles.AssumeUniversal |
            System.Globalization.DateTimeStyles.AdjustToUniversal);

        float dailyTotal = 0f;

        while (hasMore)
        {
            string url = $"https://partner.steam-api.com/IPartnerFinancialsService/GetDetailedSales/v001/?key={partnerApiKey}&date={dateString}&highwatermark_id={highwatermarkId}";

            using (UnityWebRequest request = UnityWebRequest.Get(url))
            {
                yield return request.SendWebRequest();

                if (request.result == UnityWebRequest.Result.Success)
                {
                    var salesJson = JSONNode.Parse(request.downloadHandler.text);

                    // Just use net_sales_usd from Steam API
                    foreach (JSONNode sale in salesJson["response"]["results"].AsArray)
                    {
                        float netRevenue = sale["net_sales_usd"].AsFloat;
                        dailyTotal += netRevenue;
                    }

                    ulong maxId = ulong.Parse(salesJson["response"]["max_id"]);
                    if (maxId > highwatermarkId)
                        highwatermarkId = maxId;
                    else
                        hasMore = false;
                }
                else
                {
                    Debug.LogError("Error fetching detailed sales: " + request.error);
                    hasMore = false;
                }
            }
        }

        // Store aggregated daily total
        if (salesByDate.ContainsKey(date))
        {
            salesByDate[date] += dailyTotal;
        }
        else
        {
            salesByDate[date] = dailyTotal;
        }

        if (dailyTotal > maxValue) maxValue = dailyTotal;

        datesFetched++;

        if (datesFetched >= datesToFetch)
        {
            // Fill in missing dates with $0 revenue
            FillMissingDates();

            // Recalculate maxValue from all data
            maxValue = 0f;
            foreach (var kvp in salesByDate)
            {
                if (kvp.Value > maxValue) 
                    maxValue = kvp.Value;
            }
            
            // Ensure maxValue is never zero to prevent division by zero
            if (maxValue < 0.01f) maxValue = 1f;

            // Convert dictionary to sorted list
            salesEntries = salesByDate
                .Select(kvp => new SaleEntry(kvp.Key, kvp.Value))
                .OrderBy(e => e.date)
                .ToList();

            if (rangeSlider)
            {
                rangeSlider.maxValue = salesEntries.Count;
                rangeSlider.value = rangeCount > 0 ? Mathf.Min(rangeCount, salesEntries.Count) : salesEntries.Count;
            }

            dataReady = true;
            UpdateRevenueTexts();
            CheckForNewRevenue();
        }
    }

    void FillMissingDates()
    {
        if (salesByDate.Count == 0) return;

        // Get date range
        DateTime minDate = salesByDate.Keys.Min();
        DateTime maxDate = DateTime.UtcNow.Date; // Include today

        // Fill in all dates from min to max
        for (DateTime date = minDate; date <= maxDate; date = date.AddDays(1))
        {
            if (!salesByDate.ContainsKey(date))
            {
                salesByDate[date] = 0f;
            }
        }
    }

    void CheckForNewRevenue()
    {
        float currentTotalRevenue = 0f;
        foreach (var entry in salesEntries)
        {
            currentTotalRevenue += entry.grossUSD;
        }

        // Get last stored total revenue
        float lastTotalRevenue = PlayerPrefs.GetFloat(LastTotalRevenuePrefKey, -1f);

        // If this is first launch, just store current value
        if (lastTotalRevenue < 0)
        {
            PlayerPrefs.SetFloat(LastTotalRevenuePrefKey, currentTotalRevenue);
            PlayerPrefs.Save();
            return;
        }

        // Calculate new revenue (can be negative if there were refunds)
        float newRevenue = currentTotalRevenue - lastTotalRevenue;

        // Show window for any change (positive or negative)
        if (Mathf.Abs(newRevenue) > 0.01f)
        {
            ShowNewRevenueWindow(newRevenue);
        }

        // Update stored total revenue
        PlayerPrefs.SetFloat(LastTotalRevenuePrefKey, currentTotalRevenue);
        PlayerPrefs.Save();
    }

    void ShowNewRevenueWindow(float amount)
    {   
        paused = true;
        if (newRevenueWindow && newRevenueAmountText)
        {
            newRevenueWindow.SetActive(true);
            string currencySymbol = selectedCurrency == Currency.EUR ? "€" : "$";
            float displayAmount = ConvertToDisplayCurrency(amount);
            string color = amount >= 0 ? "#00FF00" : "#FF0000"; // Green for positive, red for negative
            string prefix = amount >= 0 ? "+" : "";
            if(displayAmount > 0.001f) displayAmount = displayAmount * 0.7f; // Show estimated payment amount (70% of revenue)
            newRevenueAmountText.text = $"New Revenue!\n<color={color}>{prefix}{currencySymbol}{displayAmount:F2}</color>";
        }
    }

    void CloseNewRevenueWindow()
    {   
        paused = false;
        if (newRevenueWindow)
        {
            newRevenueWindow.SetActive(false);
        }
    }

    void OnSliderChanged(float value)
    {
        rangeCount = Mathf.RoundToInt(value);
        PlayerPrefs.SetInt(RangePrefKey, rangeCount);
        PlayerPrefs.SetInt("SliderKey", rangeCount);
        PlayerPrefs.Save();
        if (rangeSlider.value == 1)
        {
            additionalInfoText.gameObject.SetActive(true);
        }
        else
        {
            additionalInfoText.gameObject.SetActive(false);
        }
        UpdateRevenueTexts();
    }

    void OnGUI()
    {   
        if(paused)return;
        if (!dataReady || salesEntries.Count < 2) return;
        loadingScreen.SetActive(false);
        labelStyle = new GUIStyle(GUI.skin.label);
        int baseFontSize = 17;
        float scaleFactor = Screen.height / 1080f;
        labelStyle.fontSize = Mathf.RoundToInt(baseFontSize * scaleFactor);
        labelStyle.normal.textColor = Color.white;
        labelStyle.alignment = TextAnchor.MiddleCenter;
        labelStyle.font = myFont;
        labelStyle.clipping = TextClipping.Overflow;
        labelStyle.wordWrap = false;

        int displayCount = rangeCount > 0 ? Mathf.Min(rangeCount, salesEntries.Count) : salesEntries.Count;
        int startIndex = salesEntries.Count - displayCount;

        float screenWidth = Screen.width;
        float screenHeight = Screen.height;

        float chartWidth = screenWidth - paddingLeft - paddingRight;
        float chartHeight = screenHeight - paddingTop - paddingBottom;

        float previousValue = salesEntries[startIndex].grossUSD;
        Vector2 previousPoint = new Vector2(paddingLeft, paddingTop + chartHeight - (previousValue / maxValue) * chartHeight);

        for (int i = startIndex + 1; i < salesEntries.Count; i++)
        {
            float currentValue = salesEntries[i].grossUSD;
            Vector2 currentPoint = new Vector2(
                paddingLeft + ((i - startIndex) * chartWidth / (displayCount - 1)),
                paddingTop + chartHeight - (currentValue / maxValue) * chartHeight
            );

            Color color = currentValue >= previousValue ? Color.green : Color.red;
            DrawLine(previousPoint, currentPoint, color, lineWidth);

            string currencySymbol = selectedCurrency == Currency.EUR ? "€" : "$";
            float displayValue = ConvertToDisplayCurrency(currentValue);
            float displayValueClamped = Mathf.Max(displayValue, 0.01f);
            string revenueText = displayValueClamped <= 0.01f ? "" : $"{currencySymbol}{displayValue:F2}";
            Vector2 labelSize = GUI.skin.label.CalcSize(new GUIContent(revenueText));
            GUI.Label(new Rect(currentPoint.x - labelSize.x / 2, currentPoint.y - 20, labelSize.x, labelSize.y), revenueText, labelStyle);

            previousPoint = currentPoint;
            previousValue = currentValue;
        }

        UpdateDateRangeText();
    }

    void DrawLine(Vector2 pointA, Vector2 pointB, Color color, float width)
    {
        Matrix4x4 matrix = GUI.matrix;
        Color savedColor = GUI.color;

        float angle = Mathf.Atan2(pointB.y - pointA.y, pointB.x - pointA.x) * Mathf.Rad2Deg;
        float length = Vector2.Distance(pointA, pointB);

        // Safety check: prevent NaN values
        if (float.IsNaN(angle) || float.IsNaN(length) || float.IsInfinity(angle) || float.IsInfinity(length))
        {
            GUI.matrix = matrix;
            GUI.color = savedColor;
            return;
        }

        GUI.color = color;
        GUIUtility.RotateAroundPivot(angle, pointA);
        GUI.DrawTexture(new Rect(pointA.x, pointA.y, length, width), Texture2D.whiteTexture);
        GUI.matrix = matrix;
        GUI.color = savedColor;
    }

    void UpdateDateRangeText()
    {
        if (!dataReady || salesEntries.Count == 0 || rangeText == null) return;

        int displayCount = rangeCount > 0 ? Mathf.Min(rangeCount, salesEntries.Count) : salesEntries.Count;
        int startIndex = salesEntries.Count - displayCount;

        string firstDate = displayCount > 0 ? GetLocalTime(salesEntries[startIndex].date).ToString("yyyy/MM/dd") : "";
        string lastDate = displayCount > 0 ? GetLocalTime(salesEntries[salesEntries.Count - 1].date).ToString("yyyy/MM/dd") : "";

        rangeText.text = $"{firstDate} → {lastDate}";
    }

    void UpdateRevenueTexts()
    {
        if (!dataReady || salesEntries.Count == 0) return;

        int displayCount = rangeCount > 0 ? Mathf.Min(rangeCount, salesEntries.Count) : salesEntries.Count;
        int startIndex = salesEntries.Count - displayCount;

        float selectedRevenue = 0f;
        float todayRevenue = 0f;
        float weekRevenue = 0f;
        float monthRevenue = 0f;
        float yearRevenue = 0f;
        float totalRevenue = 0f;

        // Just use the last entry as "today"
        todayRevenue = salesEntries[salesEntries.Count - 1].grossUSD;

        DateTime nowUtc = DateTime.UtcNow;
        DateTime localNow = GetLocalTime(nowUtc);
        DateTime todayLocal = localNow.Date;
        DateTime weekAgoLocal = todayLocal.AddDays(-7);
        DateTime monthAgoLocal = todayLocal.AddDays(-30);
        DateTime yearAgoLocal = todayLocal.AddDays(-365);

        for (int i = 0; i < salesEntries.Count; i++)
        {
            var entry = salesEntries[i];
            totalRevenue += entry.grossUSD;

            DateTime entryLocal = GetLocalTime(entry.date).Date;

            if (entryLocal >= weekAgoLocal) weekRevenue += entry.grossUSD;
            if (entryLocal >= monthAgoLocal) monthRevenue += entry.grossUSD;
            if (entryLocal >= yearAgoLocal) yearRevenue += entry.grossUSD;

            if (i >= startIndex)
                selectedRevenue += entry.grossUSD;
        }

        string currencySymbol = selectedCurrency == Currency.EUR ? "€" : "$";
        
        if (revenueSelectedText) revenueSelectedText.text = $"Selected: {currencySymbol}{ConvertToDisplayCurrency(selectedRevenue):F2}";
        if(additionalInfoText) additionalInfoText.text = $"Today: {currencySymbol}{ConvertToDisplayCurrency(todayRevenue):F2}";
        if (revenueTodayText) revenueTodayText.text = $"Today: {currencySymbol}{ConvertToDisplayCurrency(todayRevenue):F2}";
        if (revenueWeekText) revenueWeekText.text = $"This Week: {currencySymbol}{ConvertToDisplayCurrency(weekRevenue):F2}";
        if (revenueMonthText) revenueMonthText.text = $"30 Days: {currencySymbol}{ConvertToDisplayCurrency(monthRevenue):F2}";
        if (revenueYearText) revenueYearText.text = $"This Year: {currencySymbol}{ConvertToDisplayCurrency(yearRevenue):F2}";
        if (revenueTotalText) 
            revenueTotalText.text = $"Total: {currencySymbol}{ConvertToDisplayCurrency(totalRevenue):F2}";
        if (revenueCleanTotal)
            revenueCleanTotal.text = $"Payment: <color=#00FF00>{currencySymbol}{ConvertToDisplayCurrency(totalRevenue * 0.7f):F2}</color>";
    }
}