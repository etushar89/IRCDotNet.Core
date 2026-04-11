namespace IRCDotNet.Core.Protocol;

/// <summary>
/// Standard IRC numeric reply code constants as defined in RFC 1459 and Modern IRC extensions.
/// </summary>
public static class IrcNumericReplies
{
    // Connection registration
    /// <summary>Welcome to the IRC network — registration complete.</summary>
    public const string RPL_WELCOME = "001";
    /// <summary>Server software and version information.</summary>
    public const string RPL_YOURHOST = "002";
    /// <summary>Server creation date.</summary>
    public const string RPL_CREATED = "003";
    /// <summary>Server name, version, and supported user/channel modes.</summary>
    public const string RPL_MYINFO = "004";
    /// <summary>Server feature advertisement (CASEMAPPING, CHANTYPES, PREFIX, etc.).</summary>
    public const string RPL_ISUPPORT = "005";
    /// <summary>Deprecated redirect to another server (now used as RPL_ISUPPORT alias).</summary>
    public const string RPL_BOUNCE = "010"; // Deprecated, use 005

    // User information
    /// <summary>RPL 200 — RPL_TRACELINK.</summary>
    public const string RPL_TRACELINK = "200";
    /// <summary>RPL 201 — RPL_TRACECONNECTING.</summary>
    public const string RPL_TRACECONNECTING = "201";
    /// <summary>RPL 202 — RPL_TRACEHANDSHAKE.</summary>
    public const string RPL_TRACEHANDSHAKE = "202";
    /// <summary>RPL 203 — RPL_TRACEUNKNOWN.</summary>
    public const string RPL_TRACEUNKNOWN = "203";
    /// <summary>RPL 204 — RPL_TRACEOPERATOR.</summary>
    public const string RPL_TRACEOPERATOR = "204";
    /// <summary>RPL 205 — RPL_TRACEUSER.</summary>
    public const string RPL_TRACEUSER = "205";
    /// <summary>RPL 206 — RPL_TRACESERVER.</summary>
    public const string RPL_TRACESERVER = "206";
    /// <summary>RPL 208 — RPL_TRACENEWTYPE.</summary>
    public const string RPL_TRACENEWTYPE = "208";
    /// <summary>RPL 209 — RPL_TRACECLASS.</summary>
    public const string RPL_TRACECLASS = "209";
    /// <summary>RPL 210 — RPL_TRACERECONNECT.</summary>
    public const string RPL_TRACERECONNECT = "210";
    /// <summary>RPL 211 — RPL_STATSLINKINFO.</summary>
    public const string RPL_STATSLINKINFO = "211";
    /// <summary>RPL 212 — RPL_STATSCOMMANDS.</summary>
    public const string RPL_STATSCOMMANDS = "212";
    /// <summary>RPL 213 — RPL_STATSCLINE.</summary>
    public const string RPL_STATSCLINE = "213";
    /// <summary>RPL 214 — RPL_STATSNLINE.</summary>
    public const string RPL_STATSNLINE = "214";
    /// <summary>RPL 215 — RPL_STATSILINE.</summary>
    public const string RPL_STATSILINE = "215";
    /// <summary>RPL 216 — RPL_STATSKLINE.</summary>
    public const string RPL_STATSKLINE = "216";
    /// <summary>RPL 217 — RPL_STATSQLINE.</summary>
    public const string RPL_STATSQLINE = "217";
    /// <summary>RPL 218 — RPL_STATSYLINE.</summary>
    public const string RPL_STATSYLINE = "218";
    /// <summary>RPL 219 — RPL_ENDOFSTATS.</summary>
    public const string RPL_ENDOFSTATS = "219";
    /// <summary>Current user mode string for the requesting client.</summary>
    public const string RPL_UMODEIS = "221";
    /// <summary>RPL 234 — RPL_SERVLIST.</summary>
    public const string RPL_SERVLIST = "234";
    /// <summary>RPL 235 — RPL_SERVLISTEND.</summary>
    public const string RPL_SERVLISTEND = "235";
    /// <summary>RPL 241 — RPL_STATSLLINE.</summary>
    public const string RPL_STATSLLINE = "241";
    /// <summary>RPL 242 — RPL_STATSUPTIME.</summary>
    public const string RPL_STATSUPTIME = "242";
    /// <summary>RPL 243 — RPL_STATSOLINE.</summary>
    public const string RPL_STATSOLINE = "243";
    /// <summary>RPL 244 — RPL_STATSHLINE.</summary>
    public const string RPL_STATSHLINE = "244";
    /// <summary>Network user count: visible users, invisible users, servers.</summary>
    public const string RPL_LUSERCLIENT = "251";
    /// <summary>Number of IRC operators currently online.</summary>
    public const string RPL_LUSEROP = "252";
    /// <summary>Number of unknown/unregistered connections.</summary>
    public const string RPL_LUSERUNKNOWN = "253";
    /// <summary>Number of channels currently formed on the network.</summary>
    public const string RPL_LUSERCHANNELS = "254";
    /// <summary>Local client and server count for this server.</summary>
    public const string RPL_LUSERME = "255";
    /// <summary>RPL 256 — RPL_ADMINME.</summary>
    public const string RPL_ADMINME = "256";
    /// <summary>RPL 257 — RPL_ADMINLOC1.</summary>
    public const string RPL_ADMINLOC1 = "257";
    /// <summary>RPL 258 — RPL_ADMINLOC2.</summary>
    public const string RPL_ADMINLOC2 = "258";
    /// <summary>RPL 259 — RPL_ADMINEMAIL.</summary>
    public const string RPL_ADMINEMAIL = "259";
    /// <summary>RPL 261 — RPL_TRACELOG.</summary>
    public const string RPL_TRACELOG = "261";
    /// <summary>RPL 263 — RPL_TRYAGAIN.</summary>
    public const string RPL_TRYAGAIN = "263";
    /// <summary>Target user is away — includes their away message.</summary>
    public const string RPL_AWAY = "301";
    /// <summary>Response to USERHOST command with nick/host info.</summary>
    public const string RPL_USERHOST = "302";
    /// <summary>Response to ISON command — which queried nicks are online.</summary>
    public const string RPL_ISON = "303";
    /// <summary>Confirmation that the client is no longer marked as away.</summary>
    public const string RPL_UNAWAY = "305";
    /// <summary>Confirmation that the client is now marked as away.</summary>
    public const string RPL_NOWAWAY = "306";
    /// <summary>WHOIS response: nickname, username, hostname, and real name.</summary>
    public const string RPL_WHOISUSER = "311";
    /// <summary>WHOIS response: server the user is connected to.</summary>
    public const string RPL_WHOISSERVER = "312";
    /// <summary>WHOIS response: user is an IRC operator.</summary>
    public const string RPL_WHOISOPERATOR = "313";
    /// <summary>WHOWAS response: past nickname, username, hostname, and real name.</summary>
    public const string RPL_WHOWASUSER = "314";
    /// <summary>End of WHO reply list.</summary>
    public const string RPL_ENDOFWHO = "315";
    /// <summary>WHOIS response: seconds idle and signon time.</summary>
    public const string RPL_WHOISIDLE = "317";
    /// <summary>End of WHOIS reply for a nickname.</summary>
    public const string RPL_ENDOFWHOIS = "318";
    /// <summary>WHOIS response: channels the user is in with their prefixes.</summary>
    public const string RPL_WHOISCHANNELS = "319";
    /// <summary>WHO response: channel, user, host, server, nick, flags, real name.</summary>
    public const string RPL_WHOREPLY = "352";
    /// <summary>End of WHOWAS reply for a nickname.</summary>
    public const string RPL_ENDOFWHOWAS = "369";

    // Channel information
    /// <summary>Start of channel LIST response (rarely used by modern servers).</summary>
    public const string RPL_LISTSTART = "321";
    /// <summary>Channel LIST entry: channel name, user count, and topic.</summary>
    public const string RPL_LIST = "322";
    /// <summary>End of channel LIST response.</summary>
    public const string RPL_LISTEND = "323";
    /// <summary>Current mode string for a channel.</summary>
    public const string RPL_CHANNELMODEIS = "324";
    /// <summary>RPL 325 — RPL_UNIQOPIS.</summary>
    public const string RPL_UNIQOPIS = "325";
    /// <summary>Channel has no topic set.</summary>
    public const string RPL_NOTOPIC = "331";
    /// <summary>Channel topic text.</summary>
    public const string RPL_TOPIC = "332";
    /// <summary>Who set the topic and when (nick and Unix timestamp).</summary>
    public const string RPL_TOPICWHOTIME = "333";
    /// <summary>Confirmation that an INVITE was sent to the target user.</summary>
    public const string RPL_INVITING = "341";
    /// <summary>RPL 342 — RPL_SUMMONING.</summary>
    public const string RPL_SUMMONING = "342";
    /// <summary>Ban exception list entry for a channel.</summary>
    public const string RPL_EXCEPTLIST = "348";
    /// <summary>End of ban exception list.</summary>
    public const string RPL_ENDOFEXCEPTLIST = "349";
    /// <summary>Channel NAMES list: space-separated nicknames with prefix chars.</summary>
    public const string RPL_NAMREPLY = "353";
    /// <summary>End of NAMES reply for a channel.</summary>
    public const string RPL_ENDOFNAMES = "366";
    /// <summary>Ban list entry for a channel.</summary>
    public const string RPL_BANLIST = "367";
    /// <summary>End of ban list.</summary>
    public const string RPL_ENDOFBANLIST = "368";
    /// <summary>Quiet list entry for a channel (+q mode).</summary>
    public const string RPL_QUIETLIST = "728";
    /// <summary>End of quiet list.</summary>
    public const string RPL_ENDOFQUIETLIST = "729";

    // Server information
    /// <summary>Server version string.</summary>
    public const string RPL_VERSION = "351";
    /// <summary>RPL 364 — RPL_LINKS.</summary>
    public const string RPL_LINKS = "364";
    /// <summary>RPL 365 — RPL_ENDOFLINKS.</summary>
    public const string RPL_ENDOFLINKS = "365";
    /// <summary>Server information text line.</summary>
    public const string RPL_INFO = "371";
    /// <summary>End of INFO reply.</summary>
    public const string RPL_ENDOFINFO = "374";
    /// <summary>Start of Message of the Day.</summary>
    public const string RPL_MOTDSTART = "375";
    /// <summary>Message of the Day text line.</summary>
    public const string RPL_MOTD = "372";
    /// <summary>End of Message of the Day.</summary>
    public const string RPL_ENDOFMOTD = "376";
    /// <summary>Confirmation that the client is now an IRC operator.</summary>
    public const string RPL_YOUREOPER = "381";
    /// <summary>RPL 382 — RPL_REHASHING.</summary>
    public const string RPL_REHASHING = "382";
    /// <summary>Server local time response.</summary>
    public const string RPL_TIME = "391";
    /// <summary>RPL 392 — RPL_USERSSTART.</summary>
    public const string RPL_USERSSTART = "392";
    /// <summary>RPL 393 — RPL_USERS.</summary>
    public const string RPL_USERS = "393";
    /// <summary>RPL 394 — RPL_ENDOFUSERS.</summary>
    public const string RPL_ENDOFUSERS = "394";
    /// <summary>RPL 395 — RPL_NOUSERS.</summary>
    public const string RPL_NOUSERS = "395";

    // Error replies - Client errors
    /// <summary>No such nickname or channel exists.</summary>
    public const string ERR_NOSUCHNICK = "401";
    /// <summary>No such server name.</summary>
    public const string ERR_NOSUCHSERVER = "402";
    /// <summary>No such channel name.</summary>
    public const string ERR_NOSUCHCHANNEL = "403";
    /// <summary>Cannot send message to channel (muted, banned, or not joined).</summary>
    public const string ERR_CANNOTSENDTOCHAN = "404";
    /// <summary>Client has joined the maximum number of channels.</summary>
    public const string ERR_TOOMANYCHANNELS = "405";
    /// <summary>No WHOWAS information available for that nickname.</summary>
    public const string ERR_WASNOSUCHNICK = "406";
    /// <summary>Too many recipients specified for a message.</summary>
    public const string ERR_TOOMANYTARGETS = "407";
    /// <summary>No origin specified in PING/PONG.</summary>
    public const string ERR_NOORIGIN = "409";
    /// <summary>No recipient specified for PRIVMSG/NOTICE.</summary>
    public const string ERR_NORECIPIENT = "411";
    /// <summary>No text to send in PRIVMSG/NOTICE.</summary>
    public const string ERR_NOTEXTTOSEND = "412";
    /// <summary>ERR 413 — ERR_NOTOPLEVEL.</summary>
    public const string ERR_NOTOPLEVEL = "413";
    /// <summary>ERR 414 — ERR_WILDTOPLEVEL.</summary>
    public const string ERR_WILDTOPLEVEL = "414";
    /// <summary>Server does not recognize the command.</summary>
    public const string ERR_UNKNOWNCOMMAND = "421";
    /// <summary>Server has no Message of the Day configured.</summary>
    public const string ERR_NOMOTD = "422";
    /// <summary>ERR 423 — ERR_NOADMININFO.</summary>
    public const string ERR_NOADMININFO = "423";
    /// <summary>ERR 424 — ERR_FILEERROR.</summary>
    public const string ERR_FILEERROR = "424";
    /// <summary>No nickname was supplied in a NICK command.</summary>
    public const string ERR_NONICKNAMEGIVEN = "431";
    /// <summary>Nickname contains invalid characters.</summary>
    public const string ERR_ERRONEUSNICKNAME = "432";
    /// <summary>Nickname is already in use on the network.</summary>
    public const string ERR_NICKNAMEINUSE = "433";
    /// <summary>Nickname collision detected between servers (KILL issued).</summary>
    public const string ERR_NICKCOLLISION = "436";
    /// <summary>Nickname or channel is temporarily unavailable.</summary>
    public const string ERR_UNAVAILRESOURCE = "437";
    /// <summary>Target user is not in the specified channel.</summary>
    public const string ERR_USERNOTINCHANNEL = "441";
    /// <summary>Client is not in the specified channel.</summary>
    public const string ERR_NOTONCHANNEL = "442";
    /// <summary>User is already in the channel (INVITE rejected).</summary>
    public const string ERR_USERONCHANNEL = "443";
    /// <summary>ERR 444 — ERR_NOLOGIN.</summary>
    public const string ERR_NOLOGIN = "444";
    /// <summary>ERR 445 — ERR_SUMMONDISABLED.</summary>
    public const string ERR_SUMMONDISABLED = "445";
    /// <summary>ERR 446 — ERR_USERSDISABLED.</summary>
    public const string ERR_USERSDISABLED = "446";
    /// <summary>Client must register (NICK/USER) before issuing this command.</summary>
    public const string ERR_NOTREGISTERED = "451";
    /// <summary>Not enough parameters supplied for the command.</summary>
    public const string ERR_NEEDMOREPARAMS = "461";
    /// <summary>Client cannot re-register after initial registration.</summary>
    public const string ERR_ALREADYREGISTERED = "462";
    /// <summary>ERR 463 — ERR_NOPERMFORHOST.</summary>
    public const string ERR_NOPERMFORHOST = "463";
    /// <summary>Server password is incorrect.</summary>
    public const string ERR_PASSWDMISMATCH = "464";
    /// <summary>Client is banned from the server (K-line).</summary>
    public const string ERR_YOUREBANNEDCREEP = "465";
    /// <summary>ERR 466 — ERR_YOUWILLBEBANNED.</summary>
    public const string ERR_YOUWILLBEBANNED = "466";
    /// <summary>ERR 467 — ERR_KEYSET.</summary>
    public const string ERR_KEYSET = "467";
    /// <summary>Cannot join channel — user limit reached (+l).</summary>
    public const string ERR_CHANNELISFULL = "471";
    /// <summary>Unknown channel or user mode character.</summary>
    public const string ERR_UNKNOWNMODE = "472";
    /// <summary>Cannot join channel — invite only (+i).</summary>
    public const string ERR_INVITEONLYCHAN = "473";
    /// <summary>Cannot join channel — client is banned (+b).</summary>
    public const string ERR_BANNEDFROMCHAN = "474";
    /// <summary>Cannot join channel — wrong or missing key (+k).</summary>
    public const string ERR_BADCHANNELKEY = "475";
    /// <summary>ERR 476 — ERR_BADCHANMASK.</summary>
    public const string ERR_BADCHANMASK = "476";
    /// <summary>Channel does not support modes.</summary>
    public const string ERR_NOCHANMODES = "477";
    /// <summary>Channel ban list is full.</summary>
    public const string ERR_BANLISTFULL = "478";
    /// <summary>Command requires IRC operator privileges.</summary>
    public const string ERR_NOPRIVILEGES = "481";
    /// <summary>Command requires channel operator privileges.</summary>
    public const string ERR_CHANOPRIVSNEEDED = "482";
    /// <summary>Cannot KILL a server.</summary>
    public const string ERR_CANTKILLSERVER = "483";
    /// <summary>ERR 484 — ERR_RESTRICTED.</summary>
    public const string ERR_RESTRICTED = "484";
    /// <summary>ERR 485 — ERR_UNIQOPPRIVSNEEDED.</summary>
    public const string ERR_UNIQOPPRIVSNEEDED = "485";
    /// <summary>ERR 491 — ERR_NOOPERHOST.</summary>
    public const string ERR_NOOPERHOST = "491";
    /// <summary>Unknown user mode flag.</summary>
    public const string ERR_UMODEUNKNOWNFLAG = "501";
    /// <summary>Cannot change modes for other users.</summary>
    public const string ERR_USERSDONTMATCH = "502";

    // Modern IRC extensions
    /// <summary>ERR 503 — ERR_GHOSTEDCLIENT.</summary>
    public const string ERR_GHOSTEDCLIENT = "503";
    /// <summary>ERR 504 — ERR_USERNOTONSERVER.</summary>
    public const string ERR_USERNOTONSERVER = "504";
    /// <summary>ERR 511 — ERR_SILELISTFULL.</summary>
    public const string ERR_SILELISTFULL = "511";
    /// <summary>ERR 512 — ERR_NOSUCHGLINE.</summary>
    public const string ERR_NOSUCHGLINE = "512";
    /// <summary>ERR 513 — ERR_BADPING.</summary>
    public const string ERR_BADPING = "513";
    /// <summary>ERR 514 — ERR_TOOMANYDCC.</summary>
    public const string ERR_TOOMANYDCC = "514";
    /// <summary>ERR 517 — ERR_DISABLED.</summary>
    public const string ERR_DISABLED = "517";
    /// <summary>ERR 518 — ERR_LONGMASK.</summary>
    public const string ERR_LONGMASK = "518";
    /// <summary>ERR 519 — ERR_TOOMANYUSERS.</summary>
    public const string ERR_TOOMANYUSERS = "519";
    /// <summary>ERR 520 — ERR_MASKTOOWIDE.</summary>
    public const string ERR_MASKTOOWIDE = "520";
    /// <summary>ERR 521 — ERR_WHOTRUNC.</summary>
    public const string ERR_WHOTRUNC = "521";
    /// <summary>ERR 521 — ERR_LISTSYNTAX.</summary>
    public const string ERR_LISTSYNTAX = "521";
    /// <summary>ERR 522 — ERR_WHOSYNTAX.</summary>
    public const string ERR_WHOSYNTAX = "522";
    /// <summary>ERR 523 — ERR_WHOLIMEXCEED.</summary>
    public const string ERR_WHOLIMEXCEED = "523";

    // SSL/TLS support
    /// <summary>Channel requires SSL/TLS connection (+S).</summary>
    public const string ERR_SSL_REQUIRED = "489";
    /// <summary>STARTTLS: server is ready to switch to TLS.</summary>
    public const string RPL_STARTTLS = "670";
    /// <summary>STARTTLS failed — connection cannot be upgraded.</summary>
    public const string ERR_STARTTLS = "691";

    // SASL authentication
    /// <summary>SASL: successfully logged into a services account.</summary>
    public const string RPL_LOGGEDIN = "900";
    /// <summary>SASL: logged out of services account.</summary>
    public const string RPL_LOGGEDOUT = "901";
    /// <summary>SASL: nickname is locked and cannot be changed.</summary>
    public const string ERR_NICKLOCKED = "902";
    /// <summary>SASL authentication completed successfully.</summary>
    public const string RPL_SASLSUCCESS = "903";
    /// <summary>SASL authentication failed.</summary>
    public const string ERR_SASLFAIL = "904";
    /// <summary>SASL authentication data too long.</summary>
    public const string ERR_SASLTOOLONG = "905";
    /// <summary>SASL authentication was aborted by the client.</summary>
    public const string ERR_SASLABORTED = "906";
    /// <summary>SASL: already authenticated for this connection.</summary>
    public const string ERR_SASLALREADY = "907";
    /// <summary>SASL: server lists available authentication mechanisms.</summary>
    public const string RPL_SASLMECHS = "908";

    // IRCv3 capabilities
    /// <summary>WHOIS response: actual hostname/IP of the user.</summary>
    public const string RPL_WHOISHOST = "378";
    /// <summary>WHOIS response: user modes set.</summary>
    public const string RPL_WHOISMODES = "379";
    /// <summary>WHOIS response: special status line (server-specific).</summary>
    public const string RPL_WHOISSPECIAL = "320";
    /// <summary>WHOIS response: services account name the user is logged into.</summary>
    public const string RPL_WHOISACCOUNT = "330";
    /// <summary>Alias for RPL_TOPICWHOTIME (topic setter and timestamp).</summary>
    public const string RPL_TOPICWHO = "333";
    /// <summary>Invite exception list entry for a channel.</summary>
    public const string RPL_INVEXLIST = "346";
    /// <summary>End of invite exception list.</summary>
    public const string RPL_ENDOFINVEXLIST = "347";
    /// <summary>MONITOR: one or more monitored nicknames are now online.</summary>
    public const string RPL_MONONLINE = "730";
    /// <summary>MONITOR: one or more monitored nicknames are now offline.</summary>
    public const string RPL_MONOFFLINE = "731";
    /// <summary>MONITOR: list of currently monitored nicknames.</summary>
    public const string RPL_MONLIST = "732";
    /// <summary>End of MONITOR list.</summary>
    public const string RPL_ENDOFMONLIST = "733";
    /// <summary>MONITOR list is full — cannot add more nicknames.</summary>
    public const string ERR_MONLISTFULL = "734";

    // Extended error codes
    /// <summary>ERR 524 — ERR_HELPNOTFOUND.</summary>
    public const string ERR_HELPNOTFOUND = "524";
    /// <summary>RPL 704 — RPL_HELPSTART.</summary>
    public const string RPL_HELPSTART = "704";
    /// <summary>RPL 705 — RPL_HELPTXT.</summary>
    public const string RPL_HELPTXT = "705";
    /// <summary>RPL 706 — RPL_ENDOFHELP.</summary>
    public const string RPL_ENDOFHELP = "706";
    /// <summary>Sending messages too fast to this target.</summary>
    public const string ERR_TARGETTOOFAST = "439";
    /// <summary>Service is confused (nickname conflict with a service).</summary>
    public const string ERR_SERVICECONFUSED = "435";

    /// <summary>
    /// Checks if a numeric code represents an error.
    /// </summary>
    /// <param name="numeric">The numeric code string.</param>
    /// <returns><c>true</c> if the code is an error reply.</returns>
    public static bool IsErrorCode(string numeric)
    {
        return numeric.StartsWith("4") || numeric.StartsWith("5") ||
               (numeric.StartsWith("9") && !IsSuccessCode(numeric));
    }

    /// <summary>
    /// Checks if a numeric code represents a successful response.
    /// </summary>
    /// <param name="numeric">The numeric code string.</param>
    /// <returns><c>true</c> if the code is a success reply.</returns>
    public static bool IsSuccessCode(string numeric)
    {
        return numeric.StartsWith("1") || numeric.StartsWith("2") || numeric.StartsWith("3") ||
               numeric == RPL_LOGGEDIN || numeric == RPL_LOGGEDOUT || numeric == RPL_SASLSUCCESS;
    }

    /// <summary>
    /// Gets a human-readable description for a numeric code.
    /// </summary>
    /// <param name="numeric">The numeric code string.</param>
    /// <returns>A description of the numeric reply.</returns>
    public static string GetDescription(string numeric)
    {
        return numeric switch
        {
            RPL_WELCOME => "Welcome to the IRC network",
            RPL_YOURHOST => "Your host is running this server",
            RPL_CREATED => "Server creation date",
            RPL_MYINFO => "Server information",
            RPL_ISUPPORT => "Server feature support",
            ERR_NOSUCHNICK => "No such nick/channel",
            ERR_NOSUCHSERVER => "No such server",
            ERR_NOSUCHCHANNEL => "No such channel",
            ERR_CANNOTSENDTOCHAN => "Cannot send to channel",
            ERR_TOOMANYCHANNELS => "Too many channels",
            ERR_UNKNOWNCOMMAND => "Unknown command",
            ERR_NONICKNAMEGIVEN => "No nickname given",
            ERR_ERRONEUSNICKNAME => "Erroneous nickname",
            ERR_NICKNAMEINUSE => "Nickname is already in use",
            ERR_NICKCOLLISION => "Nickname collision KILL",
            ERR_NOTONCHANNEL => "You're not on that channel",
            ERR_NEEDMOREPARAMS => "Not enough parameters",
            ERR_ALREADYREGISTERED => "You may not reregister",
            ERR_PASSWDMISMATCH => "Password incorrect",
            ERR_CHANNELISFULL => "Cannot join channel (+l)",
            ERR_INVITEONLYCHAN => "Cannot join channel (+i)",
            ERR_BANNEDFROMCHAN => "Cannot join channel (+b)",
            ERR_BADCHANNELKEY => "Cannot join channel (+k)",
            ERR_CHANOPRIVSNEEDED => "You're not channel operator",
            ERR_NOTREGISTERED => "You have not registered",
            RPL_SASLSUCCESS => "SASL authentication successful",
            ERR_SASLFAIL => "SASL authentication failed",
            _ => $"Numeric {numeric}"
        };
    }
}
