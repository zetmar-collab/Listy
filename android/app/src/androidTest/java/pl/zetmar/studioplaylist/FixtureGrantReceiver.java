package pl.zetmar.studioplaylist;

import android.content.*;
import android.net.Uri;

/** Test APK only. Runs as the fixture provider's owner and grants only synthetic documents. */
public class FixtureGrantReceiver extends BroadcastReceiver {
    @Override public void onReceive(Context context,Intent intent) {
        Uri root=Uri.parse("content://pl.zetmar.studioplaylist.test.documents");
        int flags=Intent.FLAG_GRANT_READ_URI_PERMISSION|Intent.FLAG_GRANT_WRITE_URI_PERMISSION;
        boolean grant=intent.getBooleanExtra("grant",false);
        if(grant)context.grantUriPermission("pl.zetmar.studioplaylist",root,flags|Intent.FLAG_GRANT_PREFIX_URI_PERMISSION);
        else context.revokeUriPermission("pl.zetmar.studioplaylist",root,flags);
        setResultCode(-1);setResultData(grant?"granted":"revoked");
    }
}
