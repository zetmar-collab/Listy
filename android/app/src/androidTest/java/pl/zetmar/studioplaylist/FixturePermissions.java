package pl.zetmar.studioplaylist;

import androidx.test.platform.app.InstrumentationRegistry;
import android.os.ParcelFileDescriptor;
import java.io.*;
import java.nio.charset.StandardCharsets;

/** Real URI grants, independent of permissions held by a particular emulator's shell UID. */
public final class FixturePermissions {
    private FixturePermissions() { }
    public static void grant() throws IOException {set(true);}
    public static void revoke() throws IOException {set(false);}
    private static void set(boolean grant) throws IOException {
        String pkg=InstrumentationRegistry.getInstrumentation().getContext().getPackageName();
        String command="am broadcast --receiver-foreground --include-stopped-packages -n "+pkg+"/pl.zetmar.studioplaylist.FixtureGrantReceiver --ez grant "+grant;
        try(InputStream input=new ParcelFileDescriptor.AutoCloseInputStream(InstrumentationRegistry.getInstrumentation().getUiAutomation().executeShellCommand(command));ByteArrayOutputStream output=new ByteArrayOutputStream()){
            byte[] buffer=new byte[4096];int n;while((n=input.read(buffer))!=-1)output.write(buffer,0,n);
            String result=output.toString(StandardCharsets.UTF_8.name());if(!result.contains(grant?"granted":"revoked"))throw new IOException("Fixture URI grant failed: "+result);
        }
    }
}
