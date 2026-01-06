/*
 * ================================================================================
 * TEAMS LOCAL API TOKEN PERSISTENCE - TROUBLESHOOTING GUIDE
 * ================================================================================
 * 
 * PROBLEM: Token is saved but doesn't work on next app launch
 * SOLUTION: Proper initialization order and event handler setup
 * 
 * ================================================================================
 */

/*
 * ROOT CAUSE ANALYSIS:
 * ====================
 * 
 * The original code had this initialization order:
 * 
 * 1. Create Client with autoConnect: true and saved token
 * 2. Client auto-connects immediately
 * 3. THEN set up event handlers (including TokenReceived)
 * 4. Call Connect() again
 * 
 * PROBLEMS:
 * ---------
 * - Event handlers were attached AFTER auto-connect
 * - TokenReceived event fired but no handler was listening
 * - Token was never updated in memory
 * - Double connection attempt (auto + manual)
 * 
 * ================================================================================
 */

/*
 * FIXED INITIALIZATION ORDER:
 * ============================
 * 
 * NEW ORDER:
 * ----------
 * 1. Load token from Settings.Default.Token
 * 2. Create Client with autoConnect: FALSE and saved token
 * 3. Set up ALL event handlers (including TokenReceived)
 * 4. Call Connect() with the token
 * 5. Token is used for authentication
 * 6. If new token received, save it via TokenReceived event
 * 
 * ================================================================================
 */

/*
 * CODE CHANGES:
 * ==============
 * 
 * BEFORE (BROKEN):
 * ----------------
 * Teams = new Client(autoConnect: true, token);  // Auto-connects immediately
 * Connect_Teams_API();  // Sets up handlers AFTER connection
 * 
 * AFTER (FIXED):
 * --------------
 * Teams = new Client(autoConnect: false, token);  // Don't auto-connect
 * Connect_Teams_API();  // Sets up handlers FIRST, then connects
 * 
 * ================================================================================
 */

/*
 * HOW TO VERIFY IT'S WORKING:
 * ============================
 * 
 * 1. First Launch (No Saved Token):
 *    - Output: "No saved token found"
 *    - Output: "Attempting to connect with existing token: No"
 *    - Output: "Event: TokenReceived"
 *    - Output: "Token saved to settings: [token preview]..."
 * 
 * 2. Second Launch (With Saved Token):
 *    - Output: "Loaded token from settings: [token preview]..."
 *    - Output: "Attempting to connect with existing token: Yes"
 *    - Output: "Event: Connected"
 *    - Output: "Connection established with token: Yes"
 * 
 * 3. Use Custom Button to Check Status:
 *    - Click the custom button
 *    - Check Debug Output:
 *      - IsConnected: True
 *      - Token present: Yes
 *      - Token from settings: Yes
 *      - Token preview matches Settings token preview
 * 
 * ================================================================================
 */

/*
 * TOKEN LIFECYCLE:
 * =================
 * 
 * SCENARIO 1: First Time User
 * ----------------------------
 * 1. App starts with no saved token (Settings.Default.Token = "")
 * 2. Client connects without token
 * 3. Teams prompts user to authorize
 * 4. TokenReceived event fires with new token
 * 5. Token saved to Settings.Default.Token
 * 
 * SCENARIO 2: Returning User
 * ---------------------------
 * 1. App starts, loads token from Settings.Default.Token
 * 2. Client connects WITH saved token
 * 3. If token is still valid:
 *    - Connection succeeds
 *    - No TokenReceived event (token still valid)
 * 4. If token expired:
 *    - Connection fails or prompts re-auth
 *    - TokenReceived fires with new token
 *    - New token saved to Settings
 * 
 * SCENARIO 3: Token Refresh
 * --------------------------
 * 1. App running with valid token
 * 2. Token expires or needs refresh
 * 3. Teams Local API handles refresh automatically
 * 4. TokenReceived event fires with new token
 * 5. New token saved to Settings.Default.Token
 * 
 * ================================================================================
 */

/*
 * DEBUGGING TIPS:
 * ================
 * 
 * IF TOKEN NOT PERSISTING:
 * ------------------------
 * 1. Check Debug Output for:
 *    "Token setting saved: Has value"
 * 
 * 2. Verify settings location:
 *    C:\Users\[You]\AppData\Local\Packages\[YourAppPackage]\Settings\
 * 
 * 3. Check for exceptions in Debug Output:
 *    "Error saving Token setting"
 * 
 * IF TOKEN NOT LOADING:
 * ---------------------
 * 1. Check Debug Output for:
 *    "Loaded token from settings"
 * 
 * 2. If seeing "No saved token found":
 *    - Settings were cleared
 *    - App was uninstalled/reinstalled
 *    - Different user account
 * 
 * IF CONNECTION FAILS WITH SAVED TOKEN:
 * --------------------------------------
 * 1. Token may have expired
 * 2. Check Debug Output for:
 *    "Event: ErrorReceived"
 * 
 * 3. Solution:
 *    - Clear settings: Settings.Default.Clear()
 *    - Restart app
 *    - Re-authorize
 * 
 * ================================================================================
 */

/*
 * MANUAL TOKEN MANAGEMENT:
 * =========================
 * 
 * // Read current token
 * string currentToken = Settings.Default.Token;
 * 
 * // Manually set token (for testing)
 * Settings.Default.Token = "your-test-token";
 * 
 * // Clear token (force re-authorization)
 * Settings.Default.Token = "";
 * // OR
 * Settings.Default.Clear();
 * 
 * // Check if token exists
 * bool hasToken = !string.IsNullOrEmpty(Settings.Default.Token);
 * 
 * ================================================================================
 */

/*
 * EVENT HANDLER ORDER MATTERS:
 * =============================
 * 
 * CORRECT ORDER:
 * --------------
 * 1. Create Client (autoConnect: false)
 * 2. Attach TokenReceived handler  <-- MUST BE BEFORE Connect()
 * 3. Attach other event handlers
 * 4. Call Connect()
 * 5. Events fire with handlers ready
 * 
 * WRONG ORDER (ORIGINAL BUG):
 * ---------------------------
 * 1. Create Client (autoConnect: true)
 * 2. Client auto-connects
 * 3. TokenReceived fires
 * 4. NO HANDLER YET - token lost!
 * 5. Attach TokenReceived handler  <-- TOO LATE
 * 
 * ================================================================================
 */

/*
 * ADVANCED: Token Security Considerations
 * ========================================
 * 
 * CURRENT STORAGE:
 * ----------------
 * - Stored in ApplicationDataContainer
 * - Encrypted by Windows per-user
 * - Only accessible by your app
 * - Automatically isolated per-user account
 * 
 * SECURITY BEST PRACTICES:
 * ------------------------
 * 1. ? Token stored in local app data (not roaming)
 * 2. ? Windows handles encryption
 * 3. ? No token in source code
 * 4. ? Token cleared on Settings.Clear()
 * 5. ?? Consider adding token expiration check
 * 6. ?? Consider encrypting token additionally if storing sensitive data
 * 
 * PRODUCTION RECOMMENDATIONS:
 * ---------------------------
 * - Add token expiration timestamp
 * - Implement refresh token logic
 * - Add token validation before use
 * - Handle token revocation gracefully
 * 
 * ================================================================================
 */

namespace Teams_Presence_Dynamic_Light
{
    /// <summary>
    /// This class documents the token persistence implementation
    /// </summary>
    public class TokenPersistenceDocumentation
    {
        // All documentation is above
        // Implementation is in:
        // - MainWindow.xaml.cs (Client initialization and event handlers)
        // - AppSettings.cs (Token storage)
    }
}
