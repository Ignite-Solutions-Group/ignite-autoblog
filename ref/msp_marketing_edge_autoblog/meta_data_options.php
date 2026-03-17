<?php
define ("TECH_UPDATE_SETTINGS", "tech_updates_edge_settings");
define ("TECH_UPDATE_PLUGIN_PAGE", "TECH_UPDATE_pluginPage");
define ("TECH_UPDATE_POST_STATUS", "TECH_UPDATE_POST_STATUS");
define ("TECH_UPDATE_FORCE_REFRESH", "TECH_UPDATE_FORCE_REFRESH");
define ("TECH_UPDATE_FEED", "TECH_UPDATE_FEED");
define ("TECH_UPDATE_LICENSE", "TECH_UPDATE_LICENSE");
define ("TECH_UPDATE_NOTIFICATION_EMAIL", "TECH_UPDATE_NOTIFICATION_EMAIL");
add_action( 'admin_menu', 'tech_updates_edge_add_admin_menu' );
add_action( 'admin_init', 'tech_updates_edge_settings_init' );

function tech_updates_edge_add_admin_menu(  ) { 
    add_menu_page( 'MSP Marketing Edge AutoBlog', 'MSP Marketing Edge AutoBlog', 'manage_options', 'your_tech_updates', 'tech_updates_edge_options_page' );
}

function tech_updates_edge_settings_init(  ) { 

    register_setting( TECH_UPDATE_PLUGIN_PAGE, TECH_UPDATE_SETTINGS );

    add_settings_section(
        'tech_updates_edge_pluginPage_section', 
        __( '', 'tech_updates_edge' ), 
        'tech_updates_edge_settings_section_callback', 
        TECH_UPDATE_PLUGIN_PAGE
    );

    add_settings_field( 
        TECH_UPDATE_POST_STATUS, 
        __( 'Post Status As', 'tech_updates_edge' ), 
        'tech_updates_edge_select_field_2_render', 
        TECH_UPDATE_PLUGIN_PAGE, 
        'tech_updates_edge_pluginPage_section' 
    );
    
    add_settings_field( 
        TECH_UPDATE_FORCE_REFRESH, 
        __( 'Force Refresh Now', 'tech_updates_edge' ), 
        'tech_updates_edge_select_field_1_render', 
        TECH_UPDATE_PLUGIN_PAGE, 
        'tech_updates_edge_pluginPage_section' 
    );
    
    add_settings_field( 
        TECH_UPDATE_FEED, 
        __( 'Feed', 'tech_updates_edge' ), 
        'tech_updates_edge_select_field_0_render', 
        TECH_UPDATE_PLUGIN_PAGE, 
        'tech_updates_edge_pluginPage_section' 
    );

    add_settings_field( 
        TECH_UPDATE_LICENSE, 
        __( 'Licensed Email', 'tech_updates_edge' ), 
        'tech_updates_edge_text_field_0_render', 
        TECH_UPDATE_PLUGIN_PAGE, 
        'tech_updates_edge_pluginPage_section' 
    );
    
    add_settings_field( 
        TECH_UPDATE_NOTIFICATION_EMAIL, 
        __( 'Email address for new post notifications', 'tech_updates_edge' ), 
        'tech_updates_edge_text_field_1_render', 
        TECH_UPDATE_PLUGIN_PAGE, 
        'tech_updates_edge_pluginPage_section' 
    );
    
    register_setting( TECH_UPDATE_PLUGIN_PAGE, 'your_tech_updates_agreed_terms' );   
    register_setting( TECH_UPDATE_PLUGIN_PAGE, 'your_tech_updates_author' );   
    register_setting( TECH_UPDATE_PLUGIN_PAGE, 'your_tech_updates_category' );   

}

//TECH_UPDATE_FEED
function tech_updates_edge_select_field_0_render(  ) { 

    $options = get_option( TECH_UPDATE_SETTINGS );
    ?>

    <select name='<?php echo TECH_UPDATE_SETTINGS; ?>[<?php echo TECH_UPDATE_FEED; ?>]'>
        <option value='0' <?php selected( $options[TECH_UPDATE_FEED], 0 ); ?>>Feed Off</option>
        <option value='8' <?php selected( $options[TECH_UPDATE_FEED], 8 ); ?>>BLOG ONLY – US</option>
        <option value='9' <?php selected( $options[TECH_UPDATE_FEED], 9 ); ?>>BLOG ONLY – UK / AU</option>
        <option value='7' <?php selected( $options[TECH_UPDATE_FEED], 7 ); ?>>BLOG + VIDEO – AU</option>
        <option value='5' <?php selected( $options[TECH_UPDATE_FEED], 5 ); ?>>BLOG + VIDEO – UK</option>
        <option value='6' <?php selected( $options[TECH_UPDATE_FEED], 6 ); ?>>BLOG + VIDEO – US</option>
    </select>

<?php

}

//TECH_UPDATE_LICENSE
function tech_updates_edge_text_field_0_render(  ) { 

    $options = get_option( TECH_UPDATE_SETTINGS );
    ?>

    <input type='text' id='<?php echo TECH_UPDATE_LICENSE; ?>' name='<?php echo TECH_UPDATE_SETTINGS; ?>[<?php echo TECH_UPDATE_LICENSE; ?>]' value='<?php echo $options[TECH_UPDATE_LICENSE]; ?>'> <span>(the email addressed used to register for the MSP Marketing Edge)</span>
    <?php

}

//TECH_UPDATE_NOTIFICATION_EMAIL
function tech_updates_edge_text_field_1_render(  ) { 

    $options = get_option( TECH_UPDATE_SETTINGS );
    ?>

    <input type='text' placeholder='Leave blank to stop notifications' name='<?php echo TECH_UPDATE_SETTINGS; ?>[<?php echo TECH_UPDATE_NOTIFICATION_EMAIL; ?>]' value='<?php echo $options[TECH_UPDATE_NOTIFICATION_EMAIL]; ?>'> <span>(multiple email addresses can be added, separated by a comma)</span>
    <?php

}


//TECH_UPDATE_FORCE_REFRESH
function tech_updates_edge_select_field_1_render(  ) { 
    $options = get_option( TECH_UPDATE_SETTINGS );

    ?>

    <select name='<?php echo TECH_UPDATE_SETTINGS; ?>[<?php echo TECH_UPDATE_FORCE_REFRESH; ?>]'>
        <option value='0' <?php selected( $options[TECH_UPDATE_FORCE_REFRESH], 0 ); ?>>No</option>
        <option value='1' <?php selected( $options[TECH_UPDATE_FORCE_REFRESH], 1 ); ?>>Yes</option>
    </select>
<?php
}

//TECH_UPDATE_POST_STATUS
function tech_updates_edge_select_field_2_render(  ) { 
    $options = get_option( TECH_UPDATE_SETTINGS );

    ?>

    <select name='<?php echo TECH_UPDATE_SETTINGS; ?>[<?php echo TECH_UPDATE_POST_STATUS; ?>]'>
        <option value='publish' <?php selected( $options[TECH_UPDATE_POST_STATUS], "publish" ); ?>>Published</option>
        <option value='draft' <?php selected( $options[TECH_UPDATE_POST_STATUS], "draft" ); ?>>Draft</option>
    </select>
<?php
}


function tech_updates_edge_authors()
{
    $authors = get_users( array( 'role__in' => array( 'author', 'administrator', 'editor', 'contributor' ) ) );
    $tech = new YOUR_TECH_UPDATES();    
    $admin_id = $tech->get_main_admin_id();
    $author_id = get_option('your_tech_updates_author');
    if ($author_id == "")  {
        $author_id = $admin_id;
    }

    ?>
    <table class="form-table" role="presentation">
    <tbody>
    <tr>
    <th scope="row">Author</th>
    <td>
    <select name='your_tech_updates_author'>
    <?php

    foreach ($authors as $author) {
        if (count_user_posts($author->ID) >= 0) 
        {
            $selected = "";
            if ($author_id == $author->ID)
            {
                $selected = " Selected ";
            }
            ?>
            <option <?php echo $selected; ?> value='<?php echo $author->ID; ?>'><?php echo $author->display_name; ?></option>
            <?php
        }
    }
    ?>
    </td>
    </tr>
    </tbody>
    </table>
    </select>
    <?php
}

function tech_updates_edge_categories()
{
    $categories = get_categories( array(
        'hide_empty' => 0,
        'type'      => 'post',
        'orderby'                  => 'id'
    ) );

    $cat_id = get_option('your_tech_updates_category');

    ?>
    <table class="form-table" role="presentation">
    <tbody>
    <tr>
    <th scope="row">Category</th>
    <td>
    <select name='your_tech_updates_category'>
    <?php

    foreach ($categories as $category) {
        $selected = "";
        if ($cat_id == $category->term_id)
        {
            $selected = " Selected ";
        }
        ?>
        <option <?php echo $selected; ?> value='<?php echo $category->term_id; ?>'><?php echo $category->name; ?></option>
        <?php
    }
    ?>
    </td>
    </tr>
    </tbody>
    </table>
    </select>
    <?php
}

function tech_updates_edge_settings_section_callback(  ) { 
    //echo __( 'This section description', 'tech_updates_edge' );
}

function tech_updates_edge_options_page(  ) { 
        if( !current_user_can( 'edit_users' ) ) {
            echo "You don't have permissions to edit this page.";
            return;
        }
   
        $options = get_option( TECH_UPDATE_SETTINGS );
        $email = $options[TECH_UPDATE_LICENSE];
        $forceRefresh = $options[TECH_UPDATE_FORCE_REFRESH];

        $licensed = YOUR_TECH_UPDATES::check_license($email);
        $message = "";
        if ($email != "")
        {
            $message = "<span style='color:red'>This email address isn't licensed. Please contact the vendor for more information.</span>";
            if ($licensed)
            {
                $message = "<span style='color:green'>This plugin is licensed</span>";
            }
        }
        ?>
        <a target=_blank href="https://portal.mspmarketingedge.com/"><img style="display:block; margin-top:20px; width:100%; max-width:250px; height:auto;" src="https://portal.mspmarketingedge.com/wp-content/uploads/2019/03/MSP-marketing-edge.png"/></a>
         <h1>MSP Marketing Edge Wordpress AutoBlog</h1>
                    <p>Welcome to the MSP Marketing Edge Wordpress AutoBlog. When you've configured the plugin below, your website will automatically detect and copy weekly syndicated blogs. </p>
                    <p>You can also automatically create a social media post that links to your blog page, if you set your new posts to be 'Published' and also set-up the MSP Marketing Edge SocialTools. </p>
        <h3 style="color:red; font-weight:bold;">IMPORTANT NOTES:</h3>
                    <ul style="font-weight:bold;">
                        <li>- This plugin will automatically check if there are any new plugs to copy 4 times a day.</li>
                        <li>- New plugin activations will copy across the most recent 3 blog posts only.</li>
                        <li>- This tool has no SEO benefit, due to the blog being syndicated. The system uses a canonical link and 'no index' function, to stop Google punishing you for publishing duplicate content.</li>
                        <li>- The important canonical link (that tells Google that the original post came from our generic 'yourtechupdates.com' site) will be automatically placed within the head tags of your post, but may NOT show within other plugins you use to create / show canonical links (eg Yoast).</li>
                        <li>- You can either select to post the blogs automatically (select 'Published'), or create a draft for manual checking (either way you will be notified by email). You need to select 'Published' if you wish to use this tool automatically with <a target=_blank href="https://socialtools.mspmarketingedge.com/">SocialTools.</a></li>
                    </ul>

                    <p>For set-up, troubleshooting and support, <a style="font-weight:bold;" target=_blank href="https://portal.mspmarketingedge.com/autoblog/">CLICK HERE.</a></p>
        <form action='options.php' method='post'>
            <?php
            settings_fields( TECH_UPDATE_PLUGIN_PAGE );
            do_settings_sections( TECH_UPDATE_PLUGIN_PAGE );
            tech_updates_edge_authors();
            tech_updates_edge_categories();
            echo "<p class='termsmall'><input id='your_tech_updates_agreed_terms' name='your_tech_updates_agreed_terms' type='checkbox' ";
            echo checked( esc_attr( get_option('your_tech_updates_agreed_terms')), "on" );
            echo "></input> <label for='your_tech_updates_agreed_terms'>As the owner of this website, I understand that I am ultimately responsible for the content contained within these blog posts. I plan to either systematically read the blogs after publication, or check them in draft mode before publication.</label></p>";
    
            echo $message;
            settings_fields( TECH_UPDATE_PLUGIN_PAGE );
            submit_button();
            ?>

        </form>
<script>
    document.querySelector("form").addEventListener("submit", function(e) {
        var checkBox = document.getElementById("your_tech_updates_agreed_terms");
        if(checkBox.checked == false) {
            alert("Please agree to the terms and conditions before saving.");
            e.preventDefault();    //stop form from submitting
        }
        var license = document.getElementById("<?php echo TECH_UPDATE_LICENSE; ?>");
        if(license.value == "") {
            alert("Please add your MSP Edge license email address.");
            e.preventDefault();    //stop form from submitting
        }
        
    });
</script>
<style>input[type=text] { width:100%; max-width:250px;} </style>
        <?php
        if ($forceRefresh == "1") {
            echo do_shortcode("[get_tech_posts]");
        }
}