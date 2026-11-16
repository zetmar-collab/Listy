package pl.zetmar.studioplaylist;

import android.database.*;
import android.os.*;
import android.provider.DocumentsProvider;
import android.provider.DocumentsContract.Document;
import java.io.*;

/** A real provider transport with disposable files, not a mocked ContentResolver. */
public class FakeDocumentsProvider extends DocumentsProvider {
    private File root;
    @Override public boolean onCreate(){root=new File(getContext().getFilesDir(),"provider-fixtures");return root.isDirectory() || root.mkdirs();}
    private File file(String id) throws FileNotFoundException {try {File f=id.equals("root")?root:new File(root,id.substring(5));if(!f.getCanonicalPath().equals(root.getCanonicalPath()) && !f.getCanonicalPath().startsWith(root.getCanonicalPath()+File.separator))throw new IOException("Outside fixture");return f;}catch(Exception ex){throw new FileNotFoundException(ex.toString());}}
    private String id(File f){return f.equals(root)?"root":"root/"+root.toPath().relativize(f.toPath()).toString().replace('\\','/');}
    private String name(String value) throws FileNotFoundException {if(value.isEmpty() || value.equals(".") || value.equals("..") || value.contains("/") || value.contains("\\"))throw new FileNotFoundException("Invalid name");return value;}
    @Override public boolean isChildDocument(String parent,String document){return document.startsWith(parent+"/");}
    private String[] columns(String[] p){return p==null?new String[]{Document.COLUMN_DOCUMENT_ID,Document.COLUMN_DISPLAY_NAME,Document.COLUMN_MIME_TYPE,Document.COLUMN_FLAGS}:p;}
    private void add(MatrixCursor c,File f,String[] projection){MatrixCursor.RowBuilder row=c.newRow();for(String col:projection)switch(col){case Document.COLUMN_DOCUMENT_ID:row.add(id(f));break;case Document.COLUMN_DISPLAY_NAME:row.add(f.equals(root)?"Test library":f.getName());break;case Document.COLUMN_MIME_TYPE:row.add(f.isDirectory()?Document.MIME_TYPE_DIR:"application/octet-stream");break;case Document.COLUMN_SIZE:row.add(f.getName().startsWith("unknown")?null:f.length());break;case Document.COLUMN_LAST_MODIFIED:row.add(f.getName().startsWith("unknown")?null:f.lastModified());break;case Document.COLUMN_FLAGS:row.add(Document.FLAG_SUPPORTS_DELETE|Document.FLAG_SUPPORTS_RENAME|Document.FLAG_SUPPORTS_WRITE|(f.isDirectory()?Document.FLAG_DIR_SUPPORTS_CREATE:0));break;default:row.add(null);}}
    @Override public Cursor queryRoots(String[] projection){return new MatrixCursor(new String[]{"root_id"});}
    @Override public Cursor queryDocument(String documentId,String[] projection) throws FileNotFoundException {File f=file(documentId);if(!f.exists())throw new FileNotFoundException(documentId);String[] cols=columns(projection);MatrixCursor c=new MatrixCursor(cols);add(c,f,cols);return c;}
    @Override public Cursor queryChildDocuments(String parentDocumentId,String[] projection,String sort) throws FileNotFoundException {String[] cols=columns(projection);MatrixCursor c=new MatrixCursor(cols);File[] files=file(parentDocumentId).listFiles();if(files==null)throw new FileNotFoundException(parentDocumentId);for(File f:files)add(c,f,cols);return c;}
    @Override public ParcelFileDescriptor openDocument(String documentId,String mode,CancellationSignal signal) throws FileNotFoundException{File f=file(documentId);if(mode.contains("w") && f.getName().equals("readonly.m3u8"))throw new FileNotFoundException("Injected write denial");return ParcelFileDescriptor.open(f,ParcelFileDescriptor.parseMode(mode));}
    @Override public String createDocument(String parent,String mime,String name) throws FileNotFoundException {try{File f=new File(file(parent),name(name));if(f.exists())throw new IOException("Already exists");if(Document.MIME_TYPE_DIR.equals(mime)){if(!f.mkdir())throw new IOException("mkdir failed");}else if(!f.createNewFile())throw new IOException("create failed");return id(f);}catch(Exception ex){throw new FileNotFoundException(ex.toString());}}
    @Override public String renameDocument(String document,String name) throws FileNotFoundException {File f=file(document),target=new File(f.getParentFile(),name(name));if(target.exists() || !f.renameTo(target))throw new FileNotFoundException("rename failed");return id(target);}
    @Override public void deleteDocument(String document) throws FileNotFoundException {File f=file(document);if(f.equals(root) || !f.delete())throw new FileNotFoundException("delete failed");}
}
